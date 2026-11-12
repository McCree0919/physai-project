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
    // Curriculum structure pools (LAYER3_DESIGN §17, LAYER3_C2_DESIGN §1), loaded once per process from --climb-curriculum=<pools.json>
    // (written by the editor menus Tools/Climb/Freeze Curriculum C0-C1 and C2). Each episode draws the training two-step with probability
    // twoStepShare, otherwise a structure of the active stage. Version 1: a stage is one uniform pool (structures). Version 2: a stage may
    // instead list weighted sub-pools (pools); a sub-pool is drawn with probability weight/sum(weights), then a uniform structure of it.
    // A single-pool stage consumes exactly the same random draws as version 1, so earlier runs reproduce. Validation structures are held
    // out by cluster and never drawn.
    [Serializable] public sealed class ClimbCurriculumEntry {public string id,path,sha256,cluster;}
    [Serializable] public sealed class ClimbCurriculumSubPool {public string pool;public float weight;public ClimbCurriculumEntry[] structures;}
    [Serializable] public sealed class ClimbCurriculumStage {public string name,pool;public ClimbCurriculumEntry[] structures;public ClimbCurriculumSubPool[] pools;}
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
        public sealed class SubPool
        {
            public readonly string Pool;public readonly float Weight;public readonly StructureDocument[] Docs;
            public SubPool(string pool,float weight,StructureDocument[] docs){Pool=pool;Weight=weight;Docs=docs;}
        }
        readonly List<SubPool[]> stages=new List<SubPool[]>();
        public int StageCount => stages.Count;
        // All structures of a stage, sub-pools in file order.
        public IReadOnlyList<StructureDocument> Stage(int stage) => stages[stage].SelectMany(p=>p.Docs).ToList();
        public IReadOnlyList<SubPool> SubPools(int stage) => stages[stage];

        public ClimbCurriculum(StructureDocument twoStep,float twoStepShare,IEnumerable<(string pool,StructureDocument[] docs)> stagePools)
            : this(twoStep,twoStepShare,stagePools.Select(s=>new[] {new SubPool(s.pool,1,s.docs)})) {}
        public ClimbCurriculum(StructureDocument twoStep,float twoStepShare,IEnumerable<SubPool[]> stagePools)
        {
            if(twoStepShare<0||twoStepShare>1)throw new ArgumentException("twoStepShare must be in [0,1]: "+twoStepShare);
            TwoStep=twoStep;TwoStepShare=twoStepShare;stages.AddRange(stagePools);
            if(stages.Count==0||stages.Any(s=>s.Length==0))throw new ArgumentException("Every curriculum stage needs at least one pool");
            var pools=stages.SelectMany(s=>s).ToList();
            if(pools.Any(p=>p.Docs.Length==0||p.Pool==TwoStepPool||string.IsNullOrEmpty(p.Pool)))throw new ArgumentException("Every curriculum pool needs its own name and structures");
            if(pools.Any(p=>!(p.Weight>0)||float.IsInfinity(p.Weight)))throw new ArgumentException("Curriculum pool weights must be positive and finite");
            if(stages.Any(s=>s.Select(p=>p.Pool).Distinct().Count()!=s.Length))throw new ArgumentException("Pool names repeat within a stage");
        }

        public (string pool,StructureDocument doc) Sample(int stage,System.Random random)
        {
            if(stage<0||stage>=stages.Count)throw new ArgumentOutOfRangeException(nameof(stage),$"curriculum_stage {stage} outside 0..{stages.Count-1}");
            if(random.NextDouble()<TwoStepShare)return (TwoStepPool,TwoStep);
            var pools=stages[stage];var chosen=pools[0];
            if(pools.Length>1)
            {
                // Only multi-pool stages draw a pool, so a single-pool stage keeps the version-1 random sequence.
                double u=random.NextDouble()*pools.Sum(p=>(double)p.Weight);chosen=pools[pools.Length-1];
                foreach(var p in pools){if(u<p.Weight){chosen=p;break;}u-=p.Weight;}
            }
            return (chosen.Pool,chosen.Docs[random.Next(chosen.Docs.Length)]);
        }

        static string Sha256(string text){using(var h=SHA256.Create())return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b=>b.ToString("x2")));}
        public static ClimbCurriculum Load(string path)
        {
            var file=JsonUtility.FromJson<ClimbCurriculumFile>(File.ReadAllText(path));
            if(file==null||(file.version!=1&&file.version!=2)||file.twoStep==null||file.stages==null||file.stages.Length==0)throw new InvalidDataException("Malformed curriculum file "+path);
            bool Weighted(ClimbCurriculumStage s) => s.pools!=null&&s.pools.Length>0;
            foreach(var s in file.stages)
            {
                bool flat=s.structures!=null&&s.structures.Length>0;
                if(Weighted(s)==flat)throw new InvalidDataException($"Curriculum stage {s.name} needs either structures or weighted pools, not both");
                if(Weighted(s)&&file.version<2)throw new InvalidDataException($"Weighted pools in stage {s.name} need curriculum file version 2");
            }
            var entries=file.stages.SelectMany(s=>Weighted(s)?s.pools.SelectMany(p=>p.structures??new ClimbCurriculumEntry[0]):s.structures).ToList();
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
            foreach(var e in entries)
                if(held.Contains(e.id)||(!string.IsNullOrEmpty(e.cluster)&&heldClusters.Contains(e.cluster)))throw new InvalidDataException($"Training structure {e.id} belongs to a held-out validation cluster");
            if(entries.Select(e=>e.id).Distinct().Count()!=entries.Count)throw new InvalidDataException("A structure appears in more than one curriculum pool");
            var curriculum=new ClimbCurriculum(Read(file.twoStep),file.twoStepShare,file.stages.Select(s=>Weighted(s)
                ?s.pools.Select(p=>new SubPool(p.pool,p.weight,(p.structures??new ClimbCurriculumEntry[0]).Select(Read).ToArray())).ToArray()
                :new[] {new SubPool(s.pool,1,s.structures.Select(Read).ToArray())}));
            string Start(StructureDocument d) => string.Join("|",new[] {d.start.torso,d.start.leftHand,d.start.rightHand}.Select(p=>p.Position.ToString("R")+p.Rotation.ToString("R")));
            foreach(var doc in curriculum.stages.SelectMany(s=>s).SelectMany(p=>p.Docs))
                if(Start(doc)!=Start(curriculum.TwoStep))throw new InvalidDataException($"Curriculum structure {doc.id} has a different start than the training two-step");
            return curriculum;
        }

        static bool commandLineChecked;static ClimbCurriculum commandLine;
        public static ClimbCurriculum FromCommandLine()
        {
            if(commandLineChecked)return commandLine;
            commandLineChecked=true;
            foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--climb-curriculum="))
            {
                commandLine=Load(arg.Substring("--climb-curriculum=".Length));
                Debug.Log($"CLIMB_CURRICULUM twoStepShare={commandLine.TwoStepShare} stages={string.Join(",",commandLine.stages.Select(s=>string.Join("+",s.Select(p=>$"{p.Pool}:{p.Docs.Length}@{p.Weight}"))))}");
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
                // Every episode keeps this arena's own ground start (spawn). Only the torso is compared: the scene's hands sit at the factory
                // z=-0.05, not the documents' -0.15 (the loader's reset moves rigidbodies, which the saved scene does not keep), and all
                // earlier runs started that way. Load() checks that all pool documents share one start, so the offset is the same everywhere.
                if((doc.start.torso.Position-spawn.Torso).sqrMagnitude>1e-8f||Quaternion.Angle(doc.start.torso.Rotation,spawn.Rotation)>.01f)
                    throw new InvalidOperationException("Curriculum structure "+doc.id+" has a different torso start than the arena");
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
