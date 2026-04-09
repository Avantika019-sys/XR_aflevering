using Microsoft.MixedReality.Toolkit.Utilities;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.WindowsMixedReality;

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

    private Handedness trackedHand = Handedness.Right;

    [SerializeField]
    private float speedFactor = 3.0f;

    private Vector3 speed = Vector3.zero;
    private Queue<(Vector3, float)> track = new Queue<(Vector3, float)>();
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
        Debug.Log("Started");
        mode = Modes.Board;
        gestureHandler = gestureObject.GetComponent<GestureHandler>();
        EnsureAimAssistMarker();
        SetAimAssistVisible(false);
    }

    void FixedUpdate()
    {
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
                break;
        }
        return mode;
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
        Line.SetActive(true);

        return mode;
    }

    private void generateDart()
    {
        Dart = (GameObject)Instantiate(DartPrefab, new Vector3(0, 0, 0), Quaternion.identity);
        Dart.GetComponent<DartHandler>().Pause(true);

        track.Clear();
    }

    private void trackSpeed()
    {
        float currentTime = Time.time * 1000f;

        Vector3 pos = Vector3.zero;
        if (HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, trackedHand, out MixedRealityPose palm))
            pos = palm.Position;

        track.Enqueue((pos, currentTime));

        while (track.Count > 0 && track.Peek().Item2 < currentTime - interval)
            track.Dequeue();

        currentStabilityScore = CalculateStabilityScore();
    }

    private void throwDart()
    {
        if (Dart == null)
            return;

        Vector3 dir = Vector3.zero;
        float magnitude = 0f;
        List<(Vector3, float)> points = new List<(Vector3, float)>(track);

        if (points.Count >= 2)
        {
            for (int i = 0; (i + 1) < points.Count; i++)
            {
                (Vector3, float) cur = points[i];
                (Vector3, float) next = points[i + 1];

                float deltaTime = cur.Item2 - next.Item2;
                if (Mathf.Abs(deltaTime) < 0.0001f)
                    continue;

                Vector3 temp = (cur.Item1 - next.Item1) / deltaTime;
                dir += temp;

                if (temp.magnitude > magnitude)
                    magnitude = temp.magnitude;
            }
        }

        if (dir.magnitude < 0.0001f || magnitude < 0.0001f)
        {
            Debug.Log("Throw cancelled: not enough movement data.");
            return;
        }

        speed = dir.normalized;
        speed *= magnitude;
        speed *= 1000f; // convert from units/ms to units/s
        speed *= speedFactor;
        speed *= Constants.GetComponent<ConstantsScript>().DartsSpeed;

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
        else if (angle > 20f)
            return "Too steep";
        else
            return "Good release angle";
    }

    private void moveDart()
    {
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
        }
        else
        {
            SetAimAssistVisible(false);
        }
    }

    private void EnsureAimAssistMarker()
    {
        if (aimAssistMarker == null)
        {
            aimAssistMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            aimAssistMarker.name = "AimAssistMarkerRuntime";
            aimAssistMarker.transform.localScale = Vector3.one * 0.015f;
            aimAssistMarker.layer = (int)Layers.UI;
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
        if (points.Count < 3)
            return 0f;

        List<float> speeds = new List<float>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            float deltaTimeMs = points[i + 1].Item2 - points[i].Item2;
            if (Mathf.Abs(deltaTimeMs) < 0.0001f)
                continue;

            Vector3 deltaPos = points[i + 1].Item1 - points[i].Item1;
            float speedMetersPerSecond = (deltaPos.magnitude / deltaTimeMs) * 1000f;
            speeds.Add(speedMetersPerSecond);
        }

        if (speeds.Count == 0)
            return 0f;

        float mean = speeds.Average();
        float meanAbsDeviation = 0f;
        for (int i = 0; i < speeds.Count; i++)
        {
            meanAbsDeviation += Mathf.Abs(speeds[i] - mean);
        }
        meanAbsDeviation /= speeds.Count;

        return 1f - Mathf.Clamp01(meanAbsDeviation / Mathf.Max(0.0001f, stabilityVarianceMax));
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
        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, Handedness.Right, out MixedRealityPose jointPose))
            return;

        Board.GetComponent<BoardHandler>().projectTo(jointPose.Position, jointPose.Forward);
    }
}
