import os,sys,runpy
root,config,port=sys.argv[1:4]
os.chdir(root)
sys.path[:0]=[root,os.path.join(root,'GPT_SoVITS')]
sys.argv=[os.path.join(root,'api_v2.py'),'-a','127.0.0.1','-p',port,'-c',config]
runpy.run_path(sys.argv[0],run_name='__main__')
