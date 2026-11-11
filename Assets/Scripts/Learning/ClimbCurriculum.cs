using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PhysicalAI.Structures;
using Unity.MLAgents;
using UnityEngine;

namespace PhysicalAI.Learning
{
    // Curriculum structure pools (LAYER3_DESIGN §17), loaded once per process from --climb-curriculum=<pools.json> (written by the editor
    // menu Tools/Climb/Freeze Curriculum C0-C1). Each episode draws the training two-step with probability twoStepShare, otherwise a
    // uniform structure of the active stage's pool. Validation structures are held out by cluster and never drawn.
    [Serializable] public sealed class ClimbCurriculumEntry {public string id,path,sha256,cluster;}
    [Serializable] public sealed class ClimbCurriculumStage {public string name,pool;public ClimbCurriculumEntry[] structures;}
    [Serializable] public sealed class ClimbCurriculumFile
    {
        public int version;public string root,rule;public float twoStepShare;
        public ClimbCurriculumEntry twoStep;public ClimbCurriculumStage[] stages;public ClimbCurriculumEntry[] validation;
    }

    public sealed class ClimbCurriculum
    {
        public const string TwoStepPool="two-step";
        public readonly float TwoStepShare;
        public readonly StructureDocument TwoStep;
        readonly List<(string pool,StructureDocument[] docs)> stages=new List<(string,StructureDocument[])>();
        public int StageCount => stages.Count;
        public IReadOnlyList<StructureDocument> Stage(int stage) => stages[stage].docs;
        public string PoolOf(int stage) => stages[stage].pool;

        public ClimbCurriculum(StructureDocument twoStep,float twoStepShare,IEnumerable<(string pool,StructureDocument[] docs)> stagePools)
        {
            if(twoStepShare<0||twoStepShare>1)throw new ArgumentException("twoStepShare must be in [0,1]: "+twoStepShare);
            TwoStep=twoStep;TwoStepShare=twoStepShare;stages.AddRange(stagePools);
            if(stages.Any(s=>s.docs.Length==0||s.pool==TwoStepPool))throw new ArgumentException("Every curriculum stage needs its own non-empty pool");
        }

        public (string pool,StructureDocument doc) Sample(int stage,System.Random random)
        {
            if(stage<0||stage>=stages.Count)throw new ArgumentOutOfRangeException(nameof(stage),$"curriculum_stage {stage} outside 0..{stages.Count-1}");
            if(random.NextDouble()<TwoStepShare)return (TwoStepPool,TwoStep);
            var docs=stages[stage].docs;return (stages[stage].pool,docs[random.Next(docs.Length)]);
        }

        static string Sha256(string text){using(var h=SHA256.Create())return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b=>b.ToString("x2")));}
        public static ClimbCurriculum Load(string path)
        {
            var file=JsonUtility.FromJson<ClimbCurriculumFile>(File.ReadAllText(path));
            if(file==null||file.version!=1||file.twoStep==null||file.stages==null||file.stages.Length==0)throw new InvalidDataException("Malformed curriculum file "+path);
            string root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),file.root??"."));
            StructureDocument Read(ClimbCurriculumEntry e)
            {
                string json=File.ReadAllText(Path.Combine(root,e.path));
                // Frozen, settled documents only: a changed file would bypass the settlement check made at freeze time.
                if(Sha256(json)!=e.sha256)throw new InvalidDataException($"Curriculum structure {e.id} does not match its frozen sha256");
                var doc=StructureJson.Parse(json);
                if(doc.id!=e.id)throw new InvalidDataException($"Curriculum entry {e.id} holds structure {doc.id}");
                return doc;
            }
            var held=new HashSet<string>((file.validation??new ClimbCurriculumEntry[0]).Select(e=>e.id));
            var heldClusters=new HashSet<string>((file.validation??new ClimbCurriculumEntry[0]).Select(e=>e.cluster));
            foreach(var e in file.stages.SelectMany(s=>s.structures))
                if(held.Contains(e.id)||(!string.IsNullOrEmpty(e.cluster)&&heldClusters.Contains(e.cluster)))throw new InvalidDataException($"Training structure {e.id} belongs to a held-out validation cluster");
            return new ClimbCurriculum(Read(file.twoStep),file.twoStepShare,file.stages.Select(s=>(s.pool,s.structures.Select(Read).ToArray())));
        }

        static bool commandLineChecked;static ClimbCurriculum commandLine;
        public static ClimbCurriculum FromCommandLine()
        {
            if(commandLineChecked)return commandLine;
            commandLineChecked=true;
            foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--climb-curriculum="))
            {
                commandLine=Load(arg.Substring("--climb-curriculum=".Length));
                Debug.Log($"CLIMB_CURRICULUM twoStepShare={commandLine.TwoStepShare} stages={string.Join(",",commandLine.stages.Select(s=>s.pool+":"+s.docs.Length))}");
            }
            return commandLine;
        }
    }

    public sealed partial class ClimberAgent
    {
        // Assigned by tests; otherwise loaded once per process from --climb-curriculum=<pools.json>. Null keeps the scene's own structure.
        public ClimbCurriculum Curriculum;
        // Default -1 (scene structure only) so earlier results reproduce; trainer YAML sets "curriculum_stage" per lesson.
        public int CurriculumStage=-1;
        public string StructureId {get;private set;}="";
        public string StructurePool {get;private set;}="";
        System.Random curriculumRandom;StructureDocument currentStructure;PhysicsMaterial structureMaterial;

        // Episode boundary only, after ResetBody. Returns whether demonstration spawns may be used: only on the training two-step, the one
        // structure with a recorded state library (§17.1 option A). Without a curriculum, or with curriculum_stage < 0, nothing changes.
        bool ApplyCurriculum()
        {
            var curriculum=Curriculum??ClimbCurriculum.FromCommandLine();
            int stage=(int)Academy.Instance.EnvironmentParameters.GetWithDefault("curriculum_stage",CurriculumStage);
            if(curriculum==null||stage<0){StructureId=StructurePool="";return true;}
            if(curriculumRandom==null)curriculumRandom=new System.Random(ClimbSnapshotLibrary.StableSeed(name+"#"+WorkerPort()+"#curriculum",(int)Academy.Instance.EnvironmentParameters.GetWithDefault("spawn_seed",0)));
            var (pool,doc)=curriculum.Sample(stage,curriculumRandom);
            if(doc!=currentStructure)
            {
                var start=doc.start;
                if((start.torso.Position-spawn.Torso).sqrMagnitude>1e-8f||(start.leftHand.Position-spawn.LeftHand).sqrMagnitude>1e-8f||(start.rightHand.Position-spawn.RightHand).sqrMagnitude>1e-8f)
                    throw new InvalidOperationException("Curriculum structure "+doc.id+" has a different start pose than the arena");
                if(structureMaterial==null)structureMaterial=Surfaces().Values.Select(c=>c.sharedMaterial).FirstOrDefault(m=>m!=null);
                StructureSwap.Apply(body,doc,structureMaterial);surfaces=null;currentStructure=doc;
            }
            StructureId=doc.id;StructurePool=pool;
            Academy.Instance.StatsRecorder.Add("Climb/Curriculum/Stage",stage);
            Academy.Instance.StatsRecorder.Add("Climb/Curriculum/TwoStepShare",pool==ClimbCurriculum.TwoStepPool?1:0);
            return pool==ClimbCurriculum.TwoStepPool;
        }
    }
}
