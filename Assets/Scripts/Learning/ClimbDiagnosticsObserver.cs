using System;
using System.Collections.Generic;
using System.IO;
using PhysicalAI.Climber;
using Unity.MLAgents;
using UnityEngine;

namespace PhysicalAI.Learning
{
    // Passive post-body, 50 Hz observer. Never submits a command or changes physical state.
    [DefaultExecutionOrder(100)]
    public sealed class ClimbDiagnosticsObserver : MonoBehaviour
    {
        [Serializable] public sealed class Episode
        {
            public string arena;public int agentId,episode,physicsSamples,requestDecisionSamples,requestRisingEdges,requestedHandTicks,gripSuccesses,rejectedHandTicks,cooldownBlockedTicks,rearmBlockedTicks;
            public float firstGripSeconds=-1,maximumTorsoY=.4f,maximumGripTorsoY=-1,grippedSeconds;
            public int actualTargetSamples,actualOuterBoundary,actualInnerBoundary;
            public int[] actualNormHistogram=new int[17];
            public string[] rejectionReasons;public int[] rejectionCounts;
        }
        ClimberAgent agent;ClimberBody body;Episode current;
        readonly bool[] held=new bool[2],requested=new bool[2];
        readonly Dictionary<string,int> rejected=new Dictionary<string,int>();
        int identity,previousDecisions;float previousElapsed;
        string folder;StreamWriter writer;bool identified;
        void Awake()
        {
            agent=GetComponent<ClimberAgent>();body=GetComponent<ClimberBody>();
            foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--climb-diagnostic-output="))folder=arg.Substring("--climb-diagnostic-output=".Length);
            if(string.IsNullOrEmpty(folder)){enabled=false;return;}
            Directory.CreateDirectory(folder);writer=new StreamWriter(Path.Combine(folder,name+".jsonl")){AutoFlush=true};
        }
        void FixedUpdate()
        {
            if(!identified)
            {
                // Read-only SDK identity join, after every Agent's OnEnable initialization.
                identity=(int)typeof(Agent).GetField("m_EpisodeId",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(agent);identified=true;
            }
            float elapsed=agent.Elapsed;int decisions=agent.Decisions;
            if(current!=null&&(agent.CompletedEpisodes!=current.episode||elapsed+.001f<previousElapsed||decisions<previousDecisions))Flush();
            if(current==null){current=new Episode {arena=name,agentId=identity,episode=agent.CompletedEpisodes};held[0]=held[1]=requested[0]=requested[1]=false;rejected.Clear();previousDecisions=0;}
            var b=body.GetBodyState();current.physicsSamples++;current.maximumTorsoY=Mathf.Max(current.maximumTorsoY,b.Position.y);
            if(b.GrabbedHands>0){current.grippedSeconds+=Time.fixedDeltaTime;current.maximumGripTorsoY=Mathf.Max(current.maximumGripTorsoY,b.Position.y);}
            for(int i=0;i<2;i++)
            {
                var h=body.GetHandState((HandId)i);
                if(h.GrabRequested)
                {
                    current.requestedHandTicks++;
                    if(decisions!=previousDecisions)current.requestDecisionSamples++;
                    if(!requested[i])current.requestRisingEdges++;
                }
                if(h.Grabbed&&!held[i]){current.gripSuccesses++;if(current.firstGripSeconds<0)current.firstGripSeconds=elapsed;}
                if(h.GrabRequested&&!h.Grabbed)
                {
                    if(h.NeedsRearm)current.rearmBlockedTicks++;
                    else if(h.Cooldown>0)current.cooldownBlockedTicks++;
                    else
                    {
                        var candidate=body.GetGripCandidate((HandId)i);
                        if(!candidate.Allowed){current.rejectedHandTicks++;if(!rejected.ContainsKey(candidate.Reason))rejected[candidate.Reason]=0;rejected[candidate.Reason]++;}
                    }
                }
                float radius=h.TargetOffset.magnitude;current.actualTargetSamples++;
                if(radius>=.84f)current.actualOuterBoundary++;if(radius<=.16f)current.actualInnerBoundary++;
                current.actualNormHistogram[Mathf.Clamp(Mathf.FloorToInt(radius/.05f),0,16)]++;
                held[i]=h.Grabbed;requested[i]=h.GrabRequested;
            }
            previousElapsed=elapsed;previousDecisions=decisions;
        }
        void Flush()
        {
            current.rejectionReasons=new string[rejected.Count];current.rejectionCounts=new int[rejected.Count];int i=0;
            foreach(var pair in rejected){current.rejectionReasons[i]=pair.Key;current.rejectionCounts[i++]=pair.Value;}
            writer.WriteLine(JsonUtility.ToJson(current));current=null;
        }
        void OnDestroy(){writer?.Dispose();} // Discard final partial episode; never mislabel it complete.
    }
}
