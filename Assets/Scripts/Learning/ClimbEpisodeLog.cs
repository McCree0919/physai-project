using System;
using System.IO;
using UnityEngine;

namespace PhysicalAI.Learning
{
    public static class ClimbEpisodeLog
    {
        static StreamWriter writer;
        public static void Write(string arena,bool success,bool first,bool second,int decisions,float seconds,float reward,string reason)
        {
            if(Application.isEditor)return;
            if(writer==null)
            {
                string folder="results/climb-runtime";
                foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--climb-output="))folder=arg.Substring("--climb-output=".Length);
                Directory.CreateDirectory(folder);
                writer=new StreamWriter(Path.Combine(folder,"episodes-"+System.Diagnostics.Process.GetCurrentProcess().Id+".csv")){AutoFlush=true};
                writer.WriteLine("unix_ms,arena,success,level1,level2,decisions,sim_seconds,reward,reason");
                Application.quitting+=()=>{writer?.Dispose();writer=null;};
            }
            var ci=System.Globalization.CultureInfo.InvariantCulture;
            writer.WriteLine(string.Join(",",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),arena,success?"1":"0",first?"1":"0",second?"1":"0",decisions.ToString(),seconds.ToString("F4",ci),reward.ToString("F6",ci),reason));
        }
    }
}
