using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KurisuCompanion
{
    internal sealed class PcmAudio
    {
        public float[] Samples;
        public int Rate, Channels;

        // 只接受 16 位 PCM WAV（GPT-SoVITS 的默认输出），在内存中解码，不落盘。
        public static PcmAudio Decode(byte[] bytes)
        {
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("不是 WAV 音频");
                reader.ReadInt32();
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("WAV 头错误");
                int format = 0, channels = 0, rate = 0, bits = 0;
                byte[] data = null;
                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    uint size = reader.ReadUInt32();
                    long next = reader.BaseStream.Position + size + (size % 2);
                    if (size > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("WAV 数据不完整");
                    if (id == "fmt ")
                    {
                        format = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        rate = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadUInt16();
                        bits = reader.ReadUInt16();
                    }
                    if (id == "data") data = reader.ReadBytes((int)size);
                    reader.BaseStream.Position = next;
                }
                if (format != 1 || bits != 16 || channels < 1 || channels > 2 || rate < 8000 ||
                    data == null || data.Length == 0 || data.Length % (2 * channels) != 0)
                    throw new InvalidDataException("需要 16 位 PCM WAV");
                var pcm = new PcmAudio { Rate = rate, Channels = channels, Samples = new float[data.Length / 2] };
                for (int i = 0; i < pcm.Samples.Length; i++)
                    pcm.Samples[i] = (short)(data[i * 2] | data[i * 2 + 1] << 8) / 32768f;
                return pcm;
            }
        }
    }

    // RMS 判断是否在发声；持续元音期间用长短交替的节奏开合，让口型不至于一直张着。
    internal sealed class MouthEnvelope
    {
        private static readonly float[] OpenDurations = { .18f, .26f, .16f, .22f, .20f };
        private static readonly float[] ClosedDurations = { .08f, .12f, .08f, .10f, .12f };
        private float level, phaseTime;
        private int cycle;
        private bool voiced;
        public bool Open { get; private set; }

        public bool Step(float rms, float elapsed = .04f)
        {
            elapsed = Mathf.Clamp(elapsed, 0, .2f);
            // Attack 快、Release 慢；双阈值避免在临界音量附近抖动。
            float response = rms > level ? .035f : .06f;
            level = Mathf.Lerp(level, rms, 1 - Mathf.Exp(-elapsed / response));
            bool wasVoiced = voiced;
            voiced = voiced ? level > .015f : level > .035f;
            if (!voiced) { Open = false; phaseTime = 0; return false; }
            if (!wasVoiced) { Open = true; phaseTime = 0; return true; }
            phaseTime += elapsed;
            float duration = Open ? OpenDurations[cycle] : ClosedDurations[cycle];
            if (phaseTime >= duration)
            {
                // 即使某帧耗时偏长，也保证每个开/闭状态都能被看见。
                phaseTime = 0;
                Open = !Open;
                if (Open) cycle = (cycle + 1) % OpenDurations.Length;
            }
            return Open;
        }

        public void Reset() { level = phaseTime = 0; cycle = 0; voiced = Open = false; }
    }

    // 生产者-消费者流水线：LLM 流 → 文本队列 → TTS 合成（最多预取两段）→ 音频队列 → 播放与口型。
    // 每轮对话有一个 generation 编号，打断时自增，旧协程看到编号变化即退出。
    internal sealed class SpeechPipeline : MonoBehaviour
    {
        private sealed class SpeechItem { public BilingualSentence Sentence; public PcmAudio Audio; }

        private const int MaxPrefetch = 2, FirstSegmentChars = 10;

        private readonly Queue<BilingualSentence> textQueue = new Queue<BilingualSentence>();
        private readonly Queue<SpeechItem> audioQueue = new Queue<SpeechItem>();
        private readonly MouthEnvelope envelope = new MouthEnvelope();
        private SentenceBuffer segmenter;
        private SovitsService service;
        private AudioSource source;
        private UnityWebRequest active;
        private Coroutine synth, play;
        private float[] playingSamples;
        private int channels;
        private float nextMeter, lastMeterTime;
        private int generation;
        private bool receiving, working, playing, shown, turnActive, firstTts, firstAudio;
        private BilingualSentence firstSentence, lastPresented;
        private float began;

        public Action<string> Display, Warning, PlaybackStarted, SynthesisStarted;
        public Action<bool> Mouth;
        public Action<BilingualSentence> Presented;
        public Action Finished;
        public bool Busy => receiving || working || playing || textQueue.Count > 0 || audioQueue.Count > 0;
        public float OutputLevel { get; private set; }

        public void Configure(SovitsService backend)
        {
            service = backend;
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
        }

        public void Begin(string apiKey)
        {
            Cancel();
            receiving = turnActive = true;
            shown = false;
            firstTts = firstAudio = true;
            began = Time.realtimeSinceStartup;
            segmenter = new SentenceBuffer(Mathf.Clamp(service.Settings.maxSentenceChars, 40, 60));
            int id = generation;
            synth = StartCoroutine(Synthesize(id));
            play = StartCoroutine(Play(id));
        }

        public void Push(string delta) { segmenter?.Push(delta, Enqueue); }

        private void Enqueue(string sentence) { PushBilingual(new BilingualSentence { Chinese = sentence, Complete = true }); }

        public void PushBilingual(BilingualSentence sentence)
        {
            textQueue.Enqueue(sentence);
            if (firstSentence == null) firstSentence = sentence;
            ShowFirstCaption();
        }

        // 首句中文一完整就先显示字幕，不等日语合成。
        private void ShowFirstCaption()
        {
            if (shown || firstSentence == null || !firstSentence.Complete || firstSentence.Invalid) return;
            shown = true;
            Display?.Invoke(firstSentence.Chinese);
            Present(firstSentence);
            float seconds = Time.realtimeSinceStartup - began;
            Metrics.Sample("voice.caption_seconds", seconds);
            Debug.Log("VOICE_LATENCY caption=" + seconds.ToString("F2"));
        }

        private void Present(BilingualSentence sentence)
        {
            if (ReferenceEquals(lastPresented, sentence)) return;
            lastPresented = sentence;
            Presented?.Invoke(sentence);
        }

        public void Complete()
        {
            segmenter?.Push("", Enqueue, true);
            receiving = false;
        }

        public void Cancel()
        {
            generation++;
            receiving = turnActive = false;
            active?.Abort();
            if (synth != null) StopCoroutine(synth);
            if (play != null) StopCoroutine(play);
            synth = play = null;
            active?.Dispose();
            active = null;
            textQueue.Clear();
            audioQueue.Clear();
            segmenter = null;
            firstSentence = lastPresented = null;
            working = playing = false;
            Release();
        }

        private void Release()
        {
            if (source != null)
            {
                source.Stop();
                var clip = source.clip;
                source.clip = null;
                if (clip != null) Destroy(clip);
            }
            playingSamples = null;
            OutputLevel = 0;
            envelope.Reset();
            Mouth?.Invoke(false);
        }

        private IEnumerator Synthesize(int id)
        {
            while (id == generation)
            {
                if (textQueue.Count == 0) { if (!receiving) yield break; yield return null; continue; }
                if (audioQueue.Count >= MaxPrefetch) { yield return null; continue; }
                var sentence = textQueue.Dequeue();
                working = true;
                while (service.Starting && id == generation) yield return null;
                if (!service.Ready || !service.Settings.enabled)
                {
                    audioQueue.Enqueue(new SpeechItem { Sentence = sentence });
                    working = false;
                    continue;
                }
                if (sentence.Invalid) { working = false; continue; }
                string japanese = sentence.Japanese;
                if (string.IsNullOrWhiteSpace(japanese))
                {
                    audioQueue.Enqueue(new SpeechItem { Sentence = sentence });
                    working = false;
                    continue;
                }
                // 本机 TTS 合成时间约与字数成正比，所以每轮第一句只切出约 10 字的短首段，尽早出声；
                // 首段之后的剩余部分合并为常规长度的段落，避免拆得过碎、多付每次请求的固定开销。
                var parts = new List<string>();
                int limit = Mathf.Clamp(service.Settings.maxSentenceChars, 40, 60);
                if (firstTts)
                {
                    var head = new List<string>();
                    new SentenceBuffer(FirstSegmentChars).Push(japanese, head.Add, true);
                    parts.Add(head[0]);
                    string rest = string.Concat(head.GetRange(1, head.Count - 1));
                    if (rest.Length > 0) new SentenceBuffer(limit).Push(rest, parts.Add, true);
                }
                else new SentenceBuffer(limit).Push(japanese, parts.Add, true);
                firstTts = false;
                foreach (string part in parts)
                {
                    while (audioQueue.Count >= MaxPrefetch && id == generation) yield return null;
                    if (id != generation) yield break;
                    PcmAudio pcm = null;
                    string failure = null;
                    float started = Time.realtimeSinceStartup;
                    SynthesisStarted?.Invoke(part);
                    using (var request = service.Request(part))
                    {
                        active = request;
                        yield return request.SendWebRequest();
                        active = null;
                        if (id != generation) yield break;
                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            try { pcm = PcmAudio.Decode(request.downloadHandler.data); }
                            catch (Exception e) { failure = e.Message; }
                        }
                        else
                        {
                            failure = request.error;
                            if (request.result == UnityWebRequest.Result.ConnectionError) service.Invalidate();
                        }
                    }
                    if (failure != null) Warning?.Invoke("本句语音生成失败，已跳过语音：" + failure);
                    float seconds = Time.realtimeSinceStartup - started;
                    if (pcm != null) Metrics.Sample("voice.tts_segment_seconds", seconds);
                    Debug.Log("VOICE_LATENCY tts=" + seconds.ToString("F2"));
                    audioQueue.Enqueue(new SpeechItem { Sentence = sentence, Audio = pcm });
                }
                working = false;
            }
        }

        private IEnumerator Play(int id)
        {
            while (id == generation)
            {
                if (audioQueue.Count == 0)
                {
                    if (!receiving && !working && textQueue.Count == 0) yield break;
                    yield return null;
                    continue;
                }
                var item = audioQueue.Dequeue();
                playing = true;
                // 日语可能先于中文字幕到达：等这一句配对完整再播，保证声音和字幕对应。
                while (!item.Sentence.Complete && !item.Sentence.Invalid && receiving && id == generation) yield return null;
                if (item.Sentence.Invalid || !item.Sentence.Complete) { playing = false; continue; }
                Display?.Invoke(item.Sentence.Chinese);
                Present(item.Sentence);
                if (item.Audio != null)
                {
                    playingSamples = item.Audio.Samples;
                    channels = item.Audio.Channels;
                    bool started = false;
                    try
                    {
                        source.clip = AudioClip.Create("Kurisu sentence", playingSamples.Length / channels, channels, item.Audio.Rate, false);
                        if (!source.clip.SetData(playingSamples, 0)) throw new InvalidDataException("音频缓冲写入失败");
                        source.Play();
                        started = true;
                        nextMeter = lastMeterTime = 0;
                    }
                    catch (Exception e)
                    {
                        Release();
                        Warning?.Invoke("本句音频播放失败，已保留文字：" + e.Message);
                    }
                    item.Audio = null;
                    if (started)
                    {
                        float seconds = Time.realtimeSinceStartup - began;
                        if (firstAudio) { firstAudio = false; Metrics.Sample("voice.first_audio_seconds", seconds); }
                        Debug.Log("VOICE_LATENCY playback=" + seconds.ToString("F2"));
                        PlaybackStarted?.Invoke(item.Sentence.Chinese);
                    }
                    yield return null;
                    while (source.isPlaying && id == generation) yield return null;
                    Release();
                }
                else
                {
                    // 没有语音时按字数停留一段时间，给用户读字幕。
                    float until = Time.realtimeSinceStartup + Mathf.Clamp(item.Sentence.Chinese.Length * .075f, 1.5f, 5f);
                    while (Time.realtimeSinceStartup < until && id == generation) yield return null;
                }
                playing = false;
            }
        }

        private void Update()
        {
            ShowFirstCaption();
            if (turnActive && !Busy) { turnActive = false; Finished?.Invoke(); }
            if (source == null || !source.isPlaying || playingSamples == null)
            {
                OutputLevel = 0;
                envelope.Reset();
                Mouth?.Invoke(false);
                return;
            }
            // 每 40ms 取当前播放位置附近一段 PCM 计算 RMS，驱动口型。
            if (Time.unscaledTime < nextMeter) return;
            nextMeter = Time.unscaledTime + .04f;
            int position = source.timeSamples * channels;
            int count = Math.Min((int)(source.clip.frequency * .04f) * channels, playingSamples.Length - position);
            double sum = 0;
            for (int i = 0; i < count; i++) { float x = playingSamples[position + i]; sum += x * x; }
            float audioTime = (float)source.timeSamples / source.clip.frequency;
            float elapsed = Mathf.Max(0, audioTime - lastMeterTime);
            lastMeterTime = audioTime;
            OutputLevel = count > 0 ? (float)Math.Sqrt(sum / count) : 0;
            Mouth?.Invoke(envelope.Step(OutputLevel, elapsed));
        }

        private void OnApplicationQuit() => Cancel();
        private void OnDestroy() => Cancel();
    }
}
