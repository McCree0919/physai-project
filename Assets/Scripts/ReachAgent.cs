using System;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Attach to a sphere with a Rigidbody and SphereCollider. Platform (BoxCollider),
/// agent and target must share a parent. Use a horizontal, axis-aligned platform.
/// Behavior Parameters: 5 vector observations, 2 continuous actions; add a
/// DecisionRequester with Take Actions Between Decisions enabled.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class ReachAgent : Agent
{
    public BoxCollider platform;
    public Transform target;
    [Min(0f)] public float forceMultiplier = 10f;
    [Min(0f)] public float minimumStartDistance = 2f;
    [Min(0f)] public float spawnPadding = 0.25f;
    [Min(0f)] public float fallDepth = 2f;
    [Min(1)] public int episodeMaxSteps = 1000;

    private Rigidbody body;
    private SphereCollider sphere;
    private Vector3 platformCenter;
    private Vector2 halfSize;
    private float surfaceY;
    private float agentRadius;
    private float targetHeight;
    private float spawnInset;
    private bool episodeActive;

    public override void Initialize()
    {
        body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();
        if (platform == null || target == null || transform.parent == null ||
            platform.transform.parent != transform.parent || target.parent != transform.parent)
        {
            throw new InvalidOperationException("ReachAgent: platform, agent and target must share a training-area parent.");
        }

        // Physics remains in world space; convert velocities/forces at the boundary.
        // A uniformly scaled parent preserves the spherical agent's local radius.
        Vector3 scale = transform.parent.lossyScale;
        if (scale.x <= 0f || !Mathf.Approximately(scale.x, scale.y) ||
            !Mathf.Approximately(scale.x, scale.z) ||
            Quaternion.Angle(platform.transform.localRotation, Quaternion.identity) > 0.01f ||
            Vector3.Dot(transform.parent.up, Vector3.up) < 0.999f ||
            sphere.center != Vector3.zero)
        {
            throw new InvalidOperationException("ReachAgent requires an upright, uniformly scaled training area, an unrotated platform and a centered SphereCollider.");
        }

        platformCenter = platform.transform.localPosition +
            Vector3.Scale(platform.center, platform.transform.localScale);
        Vector3 size = Vector3.Scale(platform.size, platform.transform.localScale);
        halfSize = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.z)) * 0.5f;
        surfaceY = platformCenter.y + Mathf.Abs(size.y) * 0.5f;
        Vector3 agentScale = transform.localScale;
        agentRadius = sphere.radius * Mathf.Max(Mathf.Abs(agentScale.x),
            Mathf.Abs(agentScale.y), Mathf.Abs(agentScale.z));
        Collider targetCollider = target.GetComponentInChildren<Collider>();
        if (targetCollider == null)
        {
            throw new InvalidOperationException("ReachAgent: target needs a Collider for contact detection.");
        }
        Bounds targetBounds = targetCollider.bounds;
        float targetBottom = transform.parent.InverseTransformPoint(
            new Vector3(targetBounds.center.x, targetBounds.min.y, targetBounds.center.z)).y;
        targetHeight = surfaceY + target.localPosition.y - targetBottom + 0.01f;
        float targetRadius = Mathf.Max(targetBounds.extents.x, targetBounds.extents.z) / scale.x;
        spawnInset = Mathf.Max(agentRadius, targetRadius) + spawnPadding;
        MaxStep = Mathf.Max(1, episodeMaxSteps);
    }

    public override void OnEpisodeBegin()
    {
        // MaxStep resets are issued internally by Agent. Explicit endings clear
        // this flag first, so timeout/reset failures cannot double-count success.
        if (episodeActive)
        {
            RecordResult(false);
        }

        Vector2 range = halfSize - Vector2.one * spawnInset;
        if (range.x <= 0f || range.y <= 0f || minimumStartDistance > range.magnitude * 2f)
        {
            throw new InvalidOperationException("ReachAgent: platform is too small for the spawn padding/minimum distance.");
        }

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        Vector2 start = Vector2.zero;
        Vector2 goal = Vector2.zero;
        bool found = false;
        for (int attempt = 0; attempt < 256; attempt++)
        {
            start = RandomPoint(range);
            goal = RandomPoint(range);
            if (Vector2.Distance(start, goal) >= minimumStartDistance)
            {
                found = true;
                break;
            }
        }

        // Bounded fallback guarantees separation even near the platform diagonal.
        if (!found)
        {
            start = new Vector2(UnityEngine.Random.value < 0.5f ? -range.x : range.x,
                UnityEngine.Random.value < 0.5f ? -range.y : range.y);
            goal = -start;
        }

        transform.localPosition = new Vector3(platformCenter.x + start.x,
            surfaceY + agentRadius + 0.01f, platformCenter.z + start.y);
        target.localPosition = new Vector3(platformCenter.x + goal.x,
            targetHeight, platformCenter.z + goal.y);
        episodeActive = true;
    }

    private static Vector2 RandomPoint(Vector2 range)
    {
        return new Vector2(UnityEngine.Random.Range(-range.x, range.x),
            UnityEngine.Random.Range(-range.y, range.y));
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 relative = target.localPosition - transform.localPosition;
        sensor.AddObservation(new Vector3(relative.x / (halfSize.x * 2f),
            relative.y / Mathf.Max(halfSize.x, halfSize.y) / 2f,
            relative.z / (halfSize.y * 2f)));
        Vector3 velocity = transform.parent.InverseTransformVector(body.linearVelocity);
        sensor.AddObservation(velocity.x);
        sensor.AddObservation(velocity.z);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!episodeActive)
        {
            return;
        }

        AddReward(-0.001f);
        Vector3 position = transform.localPosition;
        if (Mathf.Abs(position.x - platformCenter.x) > halfSize.x + agentRadius ||
            Mathf.Abs(position.z - platformCenter.z) > halfSize.y + agentRadius ||
            position.y < surfaceY - fallDepth)
        {
            FinishEpisode(false);
            return;
        }

        Vector3 localForce = new Vector3(Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f),
            0f, Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f));
        body.AddForce(transform.parent.TransformDirection(localForce) * forceMultiplier,
            ForceMode.Force);
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckTarget(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        CheckTarget(other);
    }

    private void CheckTarget(Collider other)
    {
        if (episodeActive && (other.transform == target || other.transform.IsChildOf(target)))
        {
            FinishEpisode(true);
        }
    }

    private void FinishEpisode(bool success)
    {
        if (!episodeActive)
        {
            return;
        }
        episodeActive = false;
        AddReward(success ? 1f : -1f);
        RecordResult(success);
        EndEpisode();
    }

    private static void RecordResult(bool success)
    {
        Academy.Instance.StatsRecorder.Add("Reach/Success", success ? 1f : 0f,
            StatAggregationMethod.Average);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> actions = actionsOut.ContinuousActions;
        actions[0] = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
            - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        actions[1] = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
            - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
    }
}
