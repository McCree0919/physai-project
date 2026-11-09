using System;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ReachArenaBuilder
{
    public const string PrefabPath = "Assets/Prefabs/ReachArena.prefab";
    public const string ScenePath = "Assets/Scenes/ReachTraining.unity";
    private const int GridSize = 4;
    private const float Spacing = 20f;

    [MenuItem("Tools/Build Reach Arena")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Stop Play mode before building Reach Arena.");
        }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EnsureFolder("Prefabs");
        EnsureFolder("Scenes");
        EnsureFolder("Materials");
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject source = new GameObject("ReachArena");
        GameObject prefab;
        try
        {
            GameObject platform = CreatePrimitive("Platform", PrimitiveType.Cube, source.transform,
                new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f));
            platform.GetComponent<Renderer>().sharedMaterial = GetMaterial("ReachPlatform", new Color(0.25f, 0.3f, 0.38f));
            PhysicsMaterial lowFriction = GetLowFrictionMaterial();
            platform.GetComponent<Collider>().sharedMaterial = lowFriction;

            GameObject target = CreatePrimitive("Target", PrimitiveType.Cube, source.transform,
                new Vector3(2f, 0.5f, 2f), Vector3.one);
            target.GetComponent<Collider>().isTrigger = true;
            target.GetComponent<Renderer>().sharedMaterial = GetMaterial("ReachTarget", new Color(0.15f, 0.85f, 0.3f));

            GameObject agentObject = CreatePrimitive("Agent", PrimitiveType.Sphere, source.transform,
                new Vector3(-2f, 0.51f, -2f), Vector3.one);
            agentObject.GetComponent<Renderer>().sharedMaterial = GetMaterial("ReachAgent", new Color(0.2f, 0.5f, 1f));
            agentObject.GetComponent<Collider>().sharedMaterial = lowFriction;
            Rigidbody body = agentObject.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            BehaviorParameters behavior = agentObject.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = "Reach";
            behavior.BehaviorType = BehaviorType.Default;
            behavior.BrainParameters.VectorObservationSize = 5;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(2);
            ReachAgent agent = agentObject.AddComponent<ReachAgent>();
            agent.platform = platform.GetComponent<BoxCollider>();
            agent.target = target.transform;
            agent.forceMultiplier = 10f;
            agent.minimumStartDistance = 2f;
            agent.episodeMaxSteps = 1000;
            agent.MaxStep = agent.episodeMaxSteps;
            DecisionRequester requester = agentObject.AddComponent<DecisionRequester>();
            requester.DecisionPeriod = 5;
            requester.TakeActionsBetweenDecisions = true;

            prefab = PrefabUtility.SaveAsPrefabAsset(source, PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException("Could not save ReachArena prefab.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
        }

        for (int row = 0; row < GridSize; row++)
        {
            for (int column = 0; column < GridSize; column++)
            {
                GameObject arena = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                arena.name = $"ReachArena_{row}_{column}";
                arena.transform.localPosition = new Vector3(
                    (column - 1.5f) * Spacing, 0f, (row - 1.5f) * Spacing);
            }
        }

        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.localPosition = new Vector3(0f, 80f, 0f);
        cameraObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 40f;
        camera.farClipPlane = 200f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
        GameObject lightObject = new GameObject("Directional Light", typeof(Light));
        lightObject.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);
        lightObject.GetComponent<Light>().type = LightType.Directional;

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            throw new InvalidOperationException("Could not save ReachTraining scene.");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Reach Arena built: {GridSize * GridSize} prefab instances, 5 observations, 2 continuous actions. Scene: {ScenePath}");
    }

    private static void EnsureFolder(string name)
    {
        if (!AssetDatabase.IsValidFolder("Assets/" + name))
        {
            AssetDatabase.CreateFolder("Assets", name);
        }
    }

    private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent,
        Vector3 position, Vector3 scale)
    {
        GameObject instance = GameObject.CreatePrimitive(type);
        instance.name = name;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = position;
        instance.transform.localScale = scale;
        return instance;
    }

    private static PhysicsMaterial GetLowFrictionMaterial()
    {
        const string path = "Assets/Materials/ReachLowFriction.physicMaterial";
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material == null)
        {
            material = new PhysicsMaterial("ReachLowFriction");
            AssetDatabase.CreateAsset(material, path);
        }
        material.staticFriction = 0.1f;
        material.dynamicFriction = 0.1f;
        material.frictionCombine = PhysicsMaterialCombine.Average;
        material.bounciness = 0f;
        material.bounceCombine = PhysicsMaterialCombine.Average;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material GetMaterial(string name, Color color)
    {
        string path = "Assets/Materials/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }
}
