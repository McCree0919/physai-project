using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PhysicalAI.Climber;
using PhysicalAI.Structures;
using UnityEngine;

public sealed class GeneratorV2Tests
{
    [Test] public void ClimberGeometryMatchesTheLiveBody()
    {
        var mat=new PhysicsMaterial("GeometryTest") {staticFriction=.15f,dynamicFriction=.15f};
        var body=ClimberArenaFactory.Create("GeometryProbe",new Vector3(200,0,200),mat,false);
        try
        {
            var torso=body.transform.Find("Torso");var hand=body.transform.Find("LeftHand");
            Assert.AreEqual(new Vector3(ClimberGeometry.TorsoWidth,ClimberGeometry.TorsoHeight,ClimberGeometry.TorsoDepth),torso.localScale);
            Assert.AreEqual(ClimberGeometry.HandRadius*2,hand.localScale.x,1e-6f);
            var b=body.GetBodyState();var h=body.GetHandState(HandId.Left);
            // Body and hand states are arena-local.
            Assert.AreEqual(ClimberGeometry.ShoulderHeight,(Quaternion.Inverse(b.Rotation)*(h.ShoulderPosition-b.Position)).y,1e-4f);
            Assert.AreEqual(ClimberGeometry.ShoulderHalfWidth,Mathf.Abs((Quaternion.Inverse(b.Rotation)*(h.ShoulderPosition-b.Position)).x),1e-4f);
            Assert.LessOrEqual(ClimberGeometry.MaxHandOffset,body.ArmLength);
            Assert.AreEqual(.451f,ClimberGeometry.MinTreadDepth,.001f);Assert.AreEqual(.678f,ClimberGeometry.MinTreadWidth,.001f);Assert.AreEqual(1.484f,ClimberGeometry.Reach,.001f);
        }
        finally{Object.DestroyImmediate(body.gameObject);Object.DestroyImmediate(mat);}
    }
    [Test] public void TrainingTwoStepAndSingleBoxAreFeasible()
    {
        var two=BoxStackFeasibility.Check(BoxStackGenerator.Smoke(true));Assert.IsTrue(two.passed,two.reason);CollectionAssert.AreEqual(new[] {"ground","lower","upper"},two.route);
        Assert.IsTrue(two.surfaces.Single(s=>s.id=="lower").required,"the 0.60 m tread must be stood on: the 1.0 m top is out of reach from the ground edge chain");
        var one=BoxStackFeasibility.Check(BoxStackGenerator.Smoke(false));Assert.IsTrue(one.passed,one.reason);
    }
    [Test] public void NarrowTreadUnderTallFaceHasNoStandingSpot()
    {
        // v1 Standard seed 10000: a 0.10 m tread in front of a 0.95 m Tall face cannot hold the torso footprint.
        var report=BoxStackFeasibility.Check(BoxStackGenerator.Generate(BoxDifficulty.Standard,10000));
        Assert.IsFalse(report.surfaces.Single(s=>s.id=="tier0").standable);
    }
    [Test] public void GeneratorV2IsValidAndFeasibleByConstruction()
    {
        foreach(BoxDifficulty difficulty in System.Enum.GetValues(typeof(BoxDifficulty)))for(int seed=0;seed<200;seed++)
        {
            var doc=BoxStackGeneratorV2.Generate(difficulty,seed);StructureJson.Parse(StructureJson.Serialize(doc));
            var report=BoxStackFeasibility.Check(doc);Assert.IsTrue(report.passed,$"{doc.id}: {report.reason}");
            foreach(var s in report.surfaces.Where(s=>s.required))Assert.GreaterOrEqual(s.freeDepth+1e-4f,ClimberGeometry.MinTreadDepth,doc.id+" "+s.id);
        }
    }
    [Test] public void GeneratorV2DrawsParametersIndependently()
    {
        // The v1 generator tied levels, offsets and islands to seed modulo small numbers (Easy had 3 distinct non-training geometries in
        // 2000 seeds). v2 must give many distinct geometries; the >=50 independent-cluster requirement itself is enforced by the freeze.
        foreach(BoxDifficulty difficulty in System.Enum.GetValues(typeof(BoxDifficulty)))
        {
            var keys=new HashSet<string>();for(int seed=0;seed<200;seed++)keys.Add(StructureJson.HashWithoutMetadata(BoxStackGeneratorV2.Generate(difficulty,seed)));
            Assert.Greater(keys.Count,difficulty==BoxDifficulty.Easy?100:150,difficulty.ToString());
        }
    }
}
