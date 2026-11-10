using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PhysicalAI.Climber.Layer2Prep
{
    [DefaultExecutionOrder(-100)]
    public sealed class PrepExperimentRunner : MonoBehaviour
    {
        public PhysicsMaterial Material;
        readonly List<PrepWall> walls=new List<PrepWall>();
        readonly List<PrepClimbController> policies=new List<PrepClimbController>();
        readonly Stopwatch clock=new Stopwatch();
        string mode,output;
        int count,caseIndex,ticks,period=5,episodes,successes;
        long physicsCount,decisionCount,startPhysics,startDecisions;
        double measureStart;
        bool measuring,finished;
        float warmup,seconds,scanMinHeight=.15f;
        int scanHeightCount=10;
        StreamWriter csv;
        readonly CultureInfo ci=CultureInfo.InvariantCulture;
        [Serializable] public sealed class Metadata
        {
            public string mode,unity,cpu,graphics,controller="PrepClimbController",sourceRevision;
            public int arenas,processorCount,decisionPeriod,episodes,successes,cases;
            public float fixedDeltaTime,timeScale;
            public double wallSeconds,warmupSeconds,measurementSeconds,globalPhysicsTicks,aggregatePhysicsSteps,aggregateDecisionSteps,globalPhysicsTicksPerSecond,aggregatePhysicsStepsPerSecond,aggregateDecisionStepsPerSecond;
        }
        string Arg(string key,string fallback)
        {
            var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]==key)return args[i+1];return fallback;
        }
        void Start()
        {
            mode=Arg("--prep-mode","validate");output=Path.GetFullPath(Arg("--prep-output","results/layer2/validate"));Directory.CreateDirectory(output);
            count=int.Parse(Arg("--prep-count",mode=="scan"?"8":"16"),ci);
            warmup=float.Parse(Arg("--prep-warmup","3"),ci);seconds=float.Parse(Arg("--prep-seconds","12"),ci);
            scanMinHeight=float.Parse(Arg("--prep-min-height",".15"),ci);
            scanHeightCount=int.Parse(Arg("--prep-height-count","10"),ci);
            Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;Time.timeScale=100;
            for(int i=0;i<count;i++)walls.Add(new PrepWall(i,Material,mode=="throughput"?0:new[]{0,15,-15,30,-30,0,15,-15}[i%8]));
            if(mode!="throughput")
            {
                csv=new StreamWriter(Path.Combine(output,"trials.csv"));
                csv.WriteLine("height_gap,lateral_gap,replica,yaw_deg,spawn_x,success,reason,detail,sim_seconds,decisions,handovers,peak_constraint_N,peak_active_N,final_x,final_y,final_z");
            }
            BeginCase();clock.Start();
            UnityEngine.Debug.Log("PREP_STARTED mode="+mode+" arenas="+count+" period="+period);
        }
        void BeginCase()
        {
            float height=mode=="scan"?scanMinHeight+(caseIndex/13)*.05f:float.Parse(Arg("--prep-height",".25"),ci);
            float spacing=mode=="scan"?.20f+(caseIndex%13)*.05f:float.Parse(Arg("--prep-spacing",".40"),ci);
            policies.Clear();
            for(int i=0;i<count;i++){walls[i].Configure(height,spacing);walls[i].Reset(mode=="throughput"?0:(i%2==0?-.01f:.01f));policies.Add(new PrepClimbController(walls[i]));}
            Physics.SyncTransforms();
        }
        void FixedUpdate()
        {
            if(finished||walls.Count==0)return;
            ticks++;physicsCount+=count;
            if(ticks%period!=0)return;
            for(int i=0;i<count;i++)
            {
                if(mode=="throughput"&&policies[i].Done)
                {
                    episodes++;if(policies[i].Success)successes++;
                    walls[i].Reset(0);policies[i]=new PrepClimbController(walls[i]);
                }
                if(!policies[i].Done){policies[i].Step(Time.fixedDeltaTime*period);decisionCount++;}
            }
        }
        void Update()
        {
            if(finished||walls.Count==0)return;
            if(mode=="throughput")
            {
                if(!measuring&&clock.Elapsed.TotalSeconds>=warmup){measuring=true;measureStart=clock.Elapsed.TotalSeconds;startPhysics=physicsCount;startDecisions=decisionCount;episodes=successes=0;}
                if(measuring&&clock.Elapsed.TotalSeconds-measureStart>=seconds)Complete();
                return;
            }
            foreach(var policy in policies)if(!policy.Done)return;
            for(int i=0;i<count;i++)
            {
                var p=policies[i];var w=walls[i];var b=w.Body.GetBodyState();episodes++;if(p.Success)successes++;
                csv.WriteLine(string.Join(",",w.HeightGap.ToString("F2",ci),w.LateralGap.ToString("F2",ci),i.ToString(),(new[]{0,15,-15,30,-30,0,15,-15}[i%8]).ToString(),(i%2==0?-.01f:.01f).ToString("F2",ci),p.Success?"1":"0",p.Reason,"\""+p.Detail.Replace("\"","\"\"")+"\"",p.Elapsed.ToString("F3",ci),p.Decisions.ToString(),p.Handovers.ToString(),p.PeakConstraint.ToString("F6",ci),p.PeakActive.ToString("F6",ci),b.Position.x.ToString("F6",ci),b.Position.y.ToString("F6",ci),b.Position.z.ToString("F6",ci)));
            }
            csv.Flush();caseIndex++;
            UnityEngine.Debug.Log("PREP_CASE "+caseIndex+" episodes="+episodes+" successes="+successes);
            if(mode!="scan"||caseIndex>=13*scanHeightCount)Complete();else BeginCase();
        }
        void Complete()
        {
            finished=true;csv?.Dispose();
            double elapsed=measuring?clock.Elapsed.TotalSeconds-measureStart:clock.Elapsed.TotalSeconds;
            long p=physicsCount-(measuring?startPhysics:0),d=decisionCount-(measuring?startDecisions:0);
            var metadata=new Metadata {mode=mode,unity=Application.unityVersion,cpu=SystemInfo.processorType,graphics=SystemInfo.graphicsDeviceType.ToString(),processorCount=SystemInfo.processorCount,
                arenas=count,decisionPeriod=period,episodes=episodes,successes=successes,cases=caseIndex,fixedDeltaTime=Time.fixedDeltaTime,timeScale=Time.timeScale,
                sourceRevision=Arg("--prep-revision","unknown"),wallSeconds=clock.Elapsed.TotalSeconds,warmupSeconds=warmup,measurementSeconds=elapsed,
                aggregatePhysicsSteps=p,globalPhysicsTicks=(double)p/count,aggregateDecisionSteps=d,
                globalPhysicsTicksPerSecond=p/count/elapsed,aggregatePhysicsStepsPerSecond=p/elapsed,aggregateDecisionStepsPerSecond=d/elapsed};
            File.WriteAllText(Path.Combine(output,"metadata.json"),JsonUtility.ToJson(metadata,true));
            UnityEngine.Debug.Log("PREP_COMPLETE "+JsonUtility.ToJson(metadata));
            Application.Quit(mode=="validate"&&successes!=count?2:0);
        }
        void OnDestroy(){csv?.Dispose();}
    }
}
