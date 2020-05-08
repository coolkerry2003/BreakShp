using PilotGaea.Geometry;
using PilotGaea.TMPEngine;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BreakShp
{
    class Program
    {
        
        static void Main(string[] args)
        {
            Console.WriteLine("基隆市全方位地理資訊系統 - 拆解圖素程式");
            Console.WriteLine("(C)2001 - 2018 基於藏識科技");
            Console.WriteLine("(C)基隆市政府 版權所有");
            Console.WriteLine("===============================");

            BreakShp breakshp = new BreakShp();
            breakshp.Generate();
            Console.ReadLine();
        }
        class BreakShp
        {
            private static string mSrc = Path.GetFullPath(ConfigurationManager.AppSettings["Src"].ToString());
            private static string mOutput = Path.GetFullPath(ConfigurationManager.AppSettings["Output"].ToString());
            private int TempServerport = 8888;
            private string TempServerPath = @"C:\ProgramData\PilotGaea\PGMaps\Test\Map.TMPX";
            private string TempServerPluginPath = @"C:\Program Files\PilotGaea\TileMap\plugins\";
            string mLogFile = ".BreakGen.log";
            private StreamWriter pLogFile;
            private Stopwatch sw = new Stopwatch();
            public void Generate()
            {
                try
                {
                    if (!Directory.Exists(mSrc))
                    {
                        Console.WriteLine("無資料來源目錄");
                        return;
                    }
                    if (!Directory.Exists(mOutput))
                    {
                        Directory.CreateDirectory(mOutput);
                        Console.WriteLine("創建目錄：" + mOutput);
                    }

                    OpenLogFile();
                    //紀錄轉檔時間
                    sw.Reset();
                    sw.Start();

                    CServer svr = new CServer();
                    if (svr.Init(TempServerport, TempServerPath, TempServerPluginPath))
                    {
                        
                        foreach (string path in Directory.GetFiles(mSrc,"*.shp"))
                        {
                            WriteLog("開始進行轉檔：" + path + "\n", true);
                            Stopwatch swShp = new Stopwatch();
                            swShp.Reset();
                            swShp.Start();

                            string srcPath = Path.GetFullPath(path);
                            string outPath = Path.Combine(mOutput, Path.GetFileName(path));

                            CSHPFile shp = svr.GeoDB.CreateSHPFile();
                            CSHPFile shp2 = svr.GeoDB.CreateSHPFile();
                            if (shp.Open(srcPath))
                            {
                                List<string> fns = new List<string>();
                                List<FIELD_TYPE> fts = new List<FIELD_TYPE>();
                                List<int> fls = new List<int>();
                                foreach (CFieldDefine d in shp.FieldDefines)
                                {
                                    fns.Add(d.Name);
                                    fts.Add(d.Type);
                                    fls.Add(d.Length);
                                }
                                if (shp2.Create(fns.ToArray(), fts.ToArray(), fls.ToArray()))
                                {
                                    CSHPEntityCollection ents = shp.GetEntities();
                                    for (int i = 0; i < ents.Count; i++)
                                    {
                                        List<GeoPolygonSet> buffer = new List<GeoPolygonSet>();
                                        GeoPolygonSet pgs = new GeoPolygonSet();
                                        if (ents[i].GetGeo(ref pgs))
                                        {
                                            OnMessage(string.Format("bounds:{0},holes:{1}\n", pgs.Bounds.Count, pgs.Holes.Count),true);
                                            string[] attrs = ents[i].GetAttrs();

                                            //重整,變碎
                                            int bcount = 0;
                                            int hcount = 0;
                                            Dictionary<int, int> holemap = new Dictionary<int, int>(pgs.Holes.Count);
                                            Dictionary<int, GeoBoundary> holeboundary = new Dictionary<int, GeoBoundary>(pgs.Holes.Count);
                                            for (int j = 0; j < pgs.Bounds.Count; j++)
                                            {
                                                GeoPolygonSet _pgs = new GeoPolygonSet();
                                                GeoPolygon pg = pgs.Bounds[j];
                                                _pgs.Bounds.Add(pg);
                                                GeoBoundary bd = pg.Boundary;

                                                object cs = new object();
                                                Parallel.For(0, pgs.Holes.Count, k =>
                                                {
                                                    if (!holemap.ContainsKey(k))
                                                    {
                                                        GeoPolygon pg2 = pgs.Holes[k];
                                                        GeoBoundary bd2 = null;
                                                        if (!holeboundary.TryGetValue(k, out bd2))
                                                        {
                                                            bd2 = pg2.Boundary;
                                                            lock (cs)
                                                            {
                                                                holeboundary.Add(k, bd2);
                                                            }
                                                        }

                                                        if (bd.IntersectRect(bd2) && pg.Include(pg2, false))
                                                        {
                                                            lock (cs)
                                                            {
                                                                _pgs.Holes.Add(pg2);
                                                                holemap.Add(k, k);
                                                            }
                                                        }
                                                    }
                                                });
                                                {
                                                    OnMessage(string.Format("{0}:,bounds:{1},holes:{2}\n", buffer.Count, _pgs.Bounds.Count, _pgs.Holes.Count),true);
                                                }
                                                buffer.Add(_pgs);
                                                bcount += _pgs.Bounds.Count;
                                                hcount += _pgs.Holes.Count;
                                                shp2.CreateEntity(_pgs, attrs);
                                            }
                                            OnMessage(string.Format("new:bounds:{0},holes:{1}\n", bcount, hcount), true);
                                            OnMessage(string.Format("new:{0}\n", buffer.Count), true);
                                        }
                                    }
                                    shp2.Save(outPath);
                                    shp2.Close();
                                }
                                shp.Close();
                            }

                            WriteLog(string.Format("花費總時程：{0} 分\n", swShp.Elapsed.TotalMinutes.ToString()), true);
                        }
                    }

                    WriteLog("花費總時程：" + sw.Elapsed.TotalMinutes.ToString() + "分\n", true);
                    CloseLogFile();
                }
                catch (Exception ex)
                {
                    WriteLog(ex.ToString(), true);
                }
            }
            /// <summary>
            /// 開啟Log檔案
            /// </summary>
            public void OpenLogFile()
            {
                string path = string.Format("{0}\\{1}", mOutput, mLogFile);
                //清空
                File.WriteAllText(path, string.Empty, Encoding.Default);
                FileStream fs = new FileStream(path, FileMode.OpenOrCreate);
                pLogFile = new StreamWriter(fs, Encoding.Default);
            }
            /// <summary>
            /// 關閉Log檔案
            /// </summary>
            public void CloseLogFile()
            {
                if (pLogFile != null)
                {
                    pLogFile.Close();
                }
            }
            /// <summary>
            /// 寫入Log
            /// </summary>
            /// <param name="_Str"></param>
            public void WriteLog(string a_Message, bool a_ShowTime)
            {
                string msg = a_ShowTime ? string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), a_Message) : a_Message;
                Console.Write(msg);
                pLogFile.Write(msg);
                pLogFile.Flush();
            }
            /// <summary>
            /// 顯示訊息
            /// </summary>
            /// <param name="a_Message"></param>
            public void OnMessage(string a_Message, bool a_ShowTime)
            {
                string msg = a_ShowTime ? string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), a_Message) : a_Message;
                Console.Write(msg);
            }
        }
    }
}
