using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using Handedness = Microsoft.MixedReality.Toolkit.Utilities.Handedness;

public enum Modes
{
    None,
    Idle,
    Board,
    Dart,
}

[AddComponentMenu("Scripts/InputController")]
public class InputController : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField]
    private float interval = 40f;

    [SerializeField]
    public Modes mode = Modes.Idle;

    [SerializeField]
    private float speedFactor = 3.0f;

    [SerializeField]
    private int minimumTrackSamples = 3;

    [SerializeField]
    private float minimumThrowVelocity = 0.05f;

    private Handedness trackedHand = Handedness.Right;

    private Vector3 speed = Vector3.zero;
    private readonly Queue<(Vector3, float)> track = new Queue<(Vector3, float)>();
    private float currentStabilityScore = 0f;

    [Header("Aim Assist")]
    [SerializeField]
    private bool enableAimAssist = true;
    [SerializeField]
    private GameObject aimAssistMarker = null;
    [SerializeField]
    private float aimAssistRayDistance = 6f;
    [SerializeField]
    private float stabilityVarianceMax = 0.8f;

    private Renderer aimAssistRenderer = null;
    private bool createdAimAssistMarkerRuntime = false;

    [Header("Gameobjects")]
    [SerializeField]
    private GameObject Board = null;
    [SerializeField]
    private GameObject Line = null;

    [SerializeField]
    private GameObject DartPrefab = null;

    private GameObject Dart = null;

    [SerializeField]
    private GameObject gestureObject = null;

    private GestureHandler gestureHandler = null;

    public GameObject Constants;

    void Start()
    {
        mode = Modes.Board;

        if (gestureObject != null)
            gestureHandler = gestureObject.GetComponent<GestureHandler>();

        EnsureAimAssistMarker();
        SetAimAssistVisible(false);
    }

    void OnEnable()
    {
        if (aimAssistMarker == null)
            EnsureAimAssistMarker();

        SetAimAssistVisible(false);
    }

    void OnDisable()
    {
        SetAimAssistVisible(false);

        if (createdAimAssistMarkerRuntime && aimAssistMarker != null)
        {
            Destroy(aimAssistMarker);
            aimAssistMarker = null;
        }
    }

    void FixedUpdate()
    {
        if (gestureHandler == null)
            return;

        switch (mode)
        {
            case Modes.Idle:
                mode = idle_state(mode);
                break;
            case Modes.Board:
                mode = board_state(mode);
                break;
            case Modes.Dart:
                mode = dart_state(mode);
                break;
            default:
                Debug.Log($"Mode undefined: {mode}");
                break;
        }
    }

    public void SetGameState(Modes mode)
    {
        this.mode = mode;
    }

    private Modes idle_state(Modes mode)
    {
        SetAimAssistVisible(false);

        switch (gestureHandler.GetGesture())
        {
            case Gesture.Pinch:
                generateDart();
                return Modes.Dart;
            default:
                return mode;
        }
    }

    private Modes board_state(Modes mode)
    {
        switch (gestureHandler.GetGesture())
        {
            case Gesture.Pinch:
                return Modes.Idle;
            default:
                break;
        }

        moveBoard();
        if (Line != null)
            Line.SetActive(false);

        SetAimAssistVisible(false);
        return mode;
    }

    private Modes dart_state(Modes mode)
    {
        switch (gestureHandler.GetGesture())
        {
            case Gesture.Pinch:
                break;
            default:
                throwDart();
                return Modes.Idle;
        }

        trackSpeed();
        moveDart();
        UpdateAimAssist();

        if (Line != null)
            Line.SetActive(true);

        return mode;
    }

    private void generateDart()
    {
        if (Dart != null)
            Destroy(Dart);

        if (DartPrefab == null)
        {
            Debug.LogError("DartPrefab is not assigned on InputController.");
            return;
        }

        Dart = Instantiate(DartPrefab, Vector3.zero, Quaternion.identity);
        Dart.GetComponent<DartHandler>().Pause(true);

        track.Clear();
        currentStabilityScore = 0f;
    }

    private void trackSpeed()
    {
        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, trackedHand, out MixedRealityPose palm))
            return;

        float currentTime = Time.time * 1000f;
        track.Enqueue((palm.Position, currentTime));

        while (track.Count > 0 && track.Peek().Item2 < currentTime - interval)
            track.Dequeue();

        currentStabilityScore = CalculateStabilityScore();
    }

    private void throwDart()
    {
        if (Dart == null)
            return;

        if (track.Count < minimumTrackSamples)
        {
            CancelPendingThrow("Throw cancelled: not enough movement samples.");
            return;
        }

        Vector3 dir = Vector3.zero;
        float magnitude = 0f;
        List<(Vector3, float)> points = track.ToList();

        for (int i = 0; (i + 1) < points.Count; i++)
        {
            (Vector3, float) current = points[i];
            (Vector3, float) next = points[i + 1];

            float deltaTimeMs = next.Item2 - current.Item2;
            if (Mathf.Abs(deltaTimeMs) < 0.0001f)
                continue;

            Vector3 temp = (next.Item1 - current.Item1) / deltaTimeMs;
            dir += temp;

            if (temp.magnitude > magnitude)
                magnitude = temp.magnitude;
        }

        if (dir.sqrMagnitude < 0.0001f || magnitude < 0.0001f)
        {
            CancelPendingThrow("Throw cancelled: not enough movement direction.");
            return;
        }

        speed = dir.normalized;
        speed *= magnitude;
        speed *= 1000f;
        speed *= speedFactor;

        if (Constants != null)
            speed *= Constants.GetComponent<ConstantsScript>().DartsSpeed;

        if (speed.magnitude < minimumThrowVelocity)
        {
            CancelPendingThrow("Throw cancelled: release velocity is too low.");
            return;
        }

        float releaseAngle = CalculateReleaseAngle(speed);
        string feedback = GetAngleFeedback(releaseAngle);

        Debug.Log($"[TRAINING] Release speed: {speed}");
        Debug.Log($"[TRAINING] Release angle: {releaseAngle:F2} degrees");
        Debug.Log($"[TRAINING] Feedback: {feedback}");
        Debug.Log($"[TRAINING] Stability score: {currentStabilityScore:F2}");

        DartHandler dart = Dart.GetComponent<DartHandler>();
        dart.SetThrowData(speed, releaseAngle, feedback, currentStabilityScore);
        dart.SetVelocity(speed);
        dart.Pause(false);

        Dart.transform.parent = null;
        Dart = null;

        track.Clear();
        SetAimAssistVisible(false);
    }

    private void CancelPendingThrow(string reason)
    {
        Debug.Log(reason);

        if (Dart != null)
        {
            Destroy(Dart);
            Dart = null;
        }

        track.Clear();
        currentStabilityScore = 0f;
        SetAimAssistVisible(false);
    }

    private float CalculateReleaseAngle(Vector3 velocity)
    {
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);

        if (horizontalVelocity.magnitude < 0.0001f)
            return 90f;

        float angle = Vector3.Angle(horizontalVelocity, velocity);

        if (velocity.y < 0f)
            angle *= -1f;

        return angle;
    }

    private string GetAngleFeedback(float angle)
    {
        if (angle < 5f)
            return "Too flat";
        if (angle > 20f)
            return "Too steep";
        return "Good release angle";
    }

    private void moveDart()
    {
        if (Dart == null)
            return;

        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.IndexKnuckle, trackedHand, out MixedRealityPose index_back))
            return;
        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.IndexTip, trackedHand, out MixedRealityPose index_front))
            return;
        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.ThumbProximalJoint, trackedHand, out MixedRealityPose thumb_back))
            return;
        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.ThumbTip, trackedHand, out MixedRealityPose thumb_front))
            return;
        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, trackedHand, out MixedRealityPose palm))
            return;

        Vector3 mid_front = midpoint(index_front.Position, thumb_front.Position, 0.4f);
        Vector3 mid_back = midpoint(index_back.Position, thumb_back.Position, 0.6f);
        Vector3 dir = (mid_front - mid_back);

        Dart.transform.position = mid_front + dir * 0.07f - palm.Right * 0.015f + palm.Up * 0.015f;
        Dart.transform.forward = dir;
    }

    private void UpdateAimAssist()
    {
        if (!enableAimAssist || Dart == null || aimAssistMarker == null)
        {
            SetAimAssistVisible(false);
            return;
        }

        int boardLayerMask = (1 << (int)Layers.Board);
        Vector3 rayOrigin = Dart.transform.position + Dart.transform.forward * 0.02f;
        Vector3 rayDirection = Dart.transform.forward.normalized;

        RaycastHit hit;
        if (Physics.Raycast(rayOrigin, rayDirection, out hit, aimAssistRayDistance, boardLayerMask))
        {
            aimAssistMarker.transform.position = hit.point + hit.normal * 0.0015f;
            aimAssistMarker.transform.forward = hit.normal;
            SetAimAssistVisible(true);
            UpdateAimAssistColor();
            return;
        }

        SetAimAssistVisible(false);
    }

    private void EnsureAimAssistMarker()
    {
        if (aimAssistMarker == null)
        {
            aimAssistMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            aimAssistMarker.name = "AimAssistMarkerRuntime";
            aimAssistMarker.transform.localScale = Vector3.one * 0.015f;
            aimAssistMarker.layer = (int)Layers.UI;
            createdAimAssistMarkerRuntime = true;

            Collider markerCollider = aimAssistMarker.GetComponent<Collider>();
            if (markerCollider != null)
                Destroy(markerCollider);
        }

        aimAssistRenderer = aimAssistMarker.GetComponent<Renderer>();
    }

    private void SetAimAssistVisible(bool visible)
    {
        if (aimAssistMarker != null && aimAssistMarker.activeSelf != visible)
            aimAssistMarker.SetActive(visible);
    }

    private void UpdateAimAssistColor()
    {
        if (aimAssistRenderer == null)
            return;

        Color color = Color.Lerp(Color.red, Color.green, Mathf.Clamp01(currentStabilityScore));
        aimAssistRenderer.material.color = color;
    }

    private float CalculateStabilityScore()
    {
        List<(Vector3, float)> points = track.ToList();
        if (points.Count < 4)
            return 0f;

        List<float> speeds = new List<float>();
        List<Vector3> directions = new List<Vector3>();

        for (int i = 0; i + 1 < points.Count; i++)
        {
            float deltaTimeMs = points[i + 1].Item2 - points[i].Item2;
            if (Mathf.Abs(deltaTimeMs) < 0.0001f)
                continue;

            Vector3 velocitySegment = ((points[i + 1].Item1 - points[i].Item1) / deltaTimeMs) * 1000f;
            float segmentSpeed = velocitySegment.magnitude;
            speeds.Add(segmentSpeed);

            if (segmentSpeed > 0.0001f)
                directions.Add(velocitySegment.normalized);
        }

        if (speeds.Count < 2)
            return 0f;

        float meanSpeed = speeds.Average();
        float meanAbsDeviation = 0f;
        for (int i = 0; i < speeds.Count; i++)
            meanAbsDeviation += Mathf.Abs(speeds[i] - meanSpeed);

        meanAbsDeviation /= speeds.Count;

        float speedConsistency = 1f - Mathf.Clamp01(meanAbsDeviation / Mathf.Max(0.0001f, stabilityVarianceMax));

        float directionConsistency = 1f;
        if (directions.Count > 1)
        {
            float dotSum = 0f;
            for (int i = 1; i < directions.Count; i++)
                dotSum += (Vector3.Dot(directions[i - 1], directions[i]) + 1f) * 0.5f;

            directionConsistency = dotSum / (directions.Count - 1);
        }

        float stability = speedConsistency * 0.7f + directionConsistency * 0.3f;
        return Mathf.Clamp01(stability);
    }

    public float GetCurrentStabilityScore()
    {
        return currentStabilityScore;
    }

    private Vector3 midpoint(Vector3 a, Vector3 b)
    {
        return midpoint(a, b, 0.5f);
    }

    private Vector3 midpoint(Vector3 a, Vector3 b, float p)
    {
        Vector3 temp = new Vector3();
        temp.x = a.x + p * (b.x - a.x);
        temp.y = a.y + p * (b.y - a.y);
        temp.z = a.z + p * (b.z - a.z);
        return temp;
    }

    private void moveBoard()
    {
        if (Board == null)
            return;

        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, trackedHand, out MixedRealityPose jointPose))
            return;

        Board.GetComponent<BoardHandler>().projectTo(jointPose.Position, jointPose.Forward);
    }
}
