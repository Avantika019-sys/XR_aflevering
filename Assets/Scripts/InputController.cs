using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Microsoft;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
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

    [SerializeField]
    [Range(0f, 1f)]
    private float throwForwardBias = 0.7f;

    [SerializeField]
    private float minThrowSpeed = 9.5f;

    [SerializeField]
    private float maxThrowSpeed = 26.0f;

    [SerializeField]
    private float releaseConfirmTime = 0.06f;

    [SerializeField]
    private int minThrowSamples = 3;

    private Vector3 speed = Vector3.zero;
    private Queue<(Vector3, float)> track = new Queue<(Vector3, float)>();

    [Header("Board Placement Settings")]
    [SerializeField]
    private float boardLockHoldTime = 2.0f;

    [SerializeField]
    private float boardStillThreshold = 0.03f;

    [SerializeField]
    private float boardRotationStillThreshold = 12f;

    [SerializeField]
    private float unstableHoldDecayMultiplier = 2.5f;

    private bool boardPlacementActive = false;
    private float openPalmHoldStart = -1f;
    private Vector3 lastBoardPosition = Vector3.zero;
    private Quaternion lastBoardRotation = Quaternion.identity;

    [Header("Reset Gesture Settings")]
    [SerializeField]
    private GameObject UIControllerObject = null;

    [SerializeField]
    private float resetGestureCooldown = 1.0f;

    [SerializeField]
    private float resetHoldTime = 0.45f;

    [SerializeField]
    private float resetStillThreshold = 0.08f;

    [SerializeField]
    private bool enableDoubleGrabReset = true;

    [SerializeField]
    private float doubleGrabMaxDelay = 1.25f;

    [SerializeField]
    private int maxDroppedDartsCollectedPerGrab = 5;

    [SerializeField]
    private float menuToggleCooldown = 0.9f;

    private float lastResetGestureTime = -10f;
    private float lastGrabTime = -10f;
    private float lastMenuToggleTime = -10f;
    private int grabCount = 0;
    private bool grabWasActiveLastFrame = false;
    private bool peaceWasActiveLastFrame = false;
    private bool resetGestureActive = false;
    private float resetGestureStart = -1f;
    private Vector3 lastResetPalmPosition = Vector3.zero;
    private float resetGestureProgress = 0f;
    private float boardLockProgress = 0f;
    private string stabilityFeedbackMessage = string.Empty;
    private float lastPinchSeenTime = -10f;

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

        EnsureRuntimeReferences();
    }

    IEnumerator waiter()
    {
        yield return new WaitForSeconds(0.05f);
        int nmb = 50;
        for (int i = 0; i < nmb; i++)
        {
            float x = Random.Range(-0.07f, 0.07f);
            float y = Random.Range(-0.07f, 0.07f);
            GameObject dart = (GameObject)Instantiate(DartPrefab, new Vector3(x, y, 0), Quaternion.identity);
            dart.GetComponent<DartHandler>().SetVelocity(new Vector3(0, 5, 7));
            if (i != nmb - 1)
                yield return new WaitForSeconds(0.05f);
        }
    }

    void FixedUpdate()
    {
        HandlePeaceMenuToggle();
        HandleResetGesture();

        if (enableDoubleGrabReset)
            HandleDoubleGrabReset();

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

        if (mode == Modes.Board)
        {
            boardPlacementActive = false;
            openPalmHoldStart = -1f;
            boardLockProgress = 0f;
            stabilityFeedbackMessage = string.Empty;
        }
    }

    public string GetStabilityFeedbackMessage()
    {
        return stabilityFeedbackMessage;
    }

    private void HandlePeaceMenuToggle()
    {
        if (UIControllerObject == null || gestureHandler == null)
            return;

        bool peaceIsActive = gestureHandler.GetGesture() == Gesture.Peace;

        if (!peaceIsActive)
        {
            peaceWasActiveLastFrame = false;
            return;
        }

        if (peaceWasActiveLastFrame)
            return;

        peaceWasActiveLastFrame = true;

        if (Time.time - lastMenuToggleTime < menuToggleCooldown)
            return;

        UIControllerScript uiController = UIControllerObject.GetComponent<UIControllerScript>();
        if (uiController == null)
            return;

        uiController.ToggleMenuVisibility();
        lastMenuToggleTime = Time.time;
        stabilityFeedbackMessage = uiController.IsMenuVisible() ? "Menu opened" : "Menu closed";
    }

    private void EnsureRuntimeReferences()
    {
        if (UIControllerObject == null)
        {
            UIControllerScript uiController = FindObjectOfType<UIControllerScript>();
            if (uiController != null)
                UIControllerObject = uiController.gameObject;
        }

        if (gestureHandler == null && gestureObject != null)
            gestureHandler = gestureObject.GetComponent<GestureHandler>();
    }

    private void TriggerResetGame()
    {
        if (Time.time - lastResetGestureTime < resetGestureCooldown)
            return;

        EnsureRuntimeReferences();

        if (UIControllerObject != null)
        {
            UIControllerScript uiController = UIControllerObject.GetComponent<UIControllerScript>();
            if (uiController != null)
            {
                uiController.ResetGame();
                Debug.Log("Full reset triggered");
                lastResetGestureTime = Time.time;
                ResetResetGestureTracking();
                return;
            }

            Debug.Log("Full reset trigger could not find UIControllerScript");
        }
        else
        {
            Debug.LogWarning("UIControllerObject is not assigned in InputController.");
        }
    }

    private void TriggerResetDarts()
    {
        if (Time.time - lastResetGestureTime < resetGestureCooldown)
            return;

        EnsureRuntimeReferences();

        if (UIControllerObject != null)
        {
            UIControllerScript uiController = UIControllerObject.GetComponent<UIControllerScript>();
            if (uiController != null && uiController.ResetSingleDart())
            {
                Debug.Log("Single dart reset triggered");
                lastResetGestureTime = Time.time;
                ResetResetGestureTracking();
                return;
            }

            Debug.Log("Single dart reset trigger found no removable dart");
        }
        else
        {
            Debug.LogWarning("UIControllerObject is not assigned in InputController.");
        }
    }

    private void TriggerCollectDroppedDarts()
    {
        if (Time.time - lastResetGestureTime < resetGestureCooldown)
            return;

        EnsureRuntimeReferences();

        if (UIControllerObject == null)
        {
            Debug.LogWarning("UIControllerObject is not assigned in InputController.");
            return;
        }

        UIControllerScript uiController = UIControllerObject.GetComponent<UIControllerScript>();
        if (uiController == null)
        {
            Debug.Log("Dropped dart collection could not find UIControllerScript");
            return;
        }

        int collectedCount = uiController.CollectDroppedDarts(maxDroppedDartsCollectedPerGrab);
        if (collectedCount <= 0)
        {
            Debug.Log("Dropped dart collection found no ground darts to remove");
            return;
        }

        lastResetGestureTime = Time.time;
        stabilityFeedbackMessage = $"Collected {collectedCount} dropped dart(s)";
        ResetResetGestureTracking();
    }

    private void HandleResetGesture()
    {
        if (mode == Modes.Board)
        {
            ResetResetGestureTracking();
            return;
        }

        if (!CanResetGame())
        {
            ResetResetGestureTracking();
            return;
        }

        if (Time.time - lastResetGestureTime < resetGestureCooldown)
        {
            ResetResetGestureTracking();
            return;
        }

        if (!IsResetOpenPalmActive())
        {
            ResetResetGestureTracking();
            return;
        }

        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, trackedHand, out MixedRealityPose palmPose))
        {
            ResetResetGestureTracking();
            return;
        }

        if (!resetGestureActive)
        {
            resetGestureActive = true;
            resetGestureStart = Time.time;
            lastResetPalmPosition = palmPose.Position;
            resetGestureProgress = 0f;
            stabilityFeedbackMessage = "Open hand flat to reset game";
            return;
        }

        float palmMovement = Vector3.Distance(palmPose.Position, lastResetPalmPosition);
        bool palmIsStable = palmMovement <= resetStillThreshold;
        lastResetPalmPosition = palmPose.Position;

        float holdDelta = Time.fixedDeltaTime / resetHoldTime;
        if (palmIsStable)
        {
            resetGestureProgress = Mathf.Clamp01(resetGestureProgress + holdDelta);
        }
        else
        {
            resetGestureProgress = Mathf.Clamp01(resetGestureProgress - holdDelta * unstableHoldDecayMultiplier);
        }

        resetGestureStart = Time.time - (resetGestureProgress * resetHoldTime);
        stabilityFeedbackMessage = palmIsStable
            ? $"Reset game: {Mathf.RoundToInt(resetGestureProgress * 100f)}%"
            : $"Keep open hand still: {Mathf.RoundToInt(resetGestureProgress * 100f)}%";

        if (resetGestureProgress >= 1f)
        {
            TriggerResetGame();
        }
    }

    private void ResetResetGestureTracking()
    {
        resetGestureActive = false;
        resetGestureStart = -1f;
        resetGestureProgress = 0f;

        if (mode != Modes.Board)
        {
            stabilityFeedbackMessage = string.Empty;
        }
    }

    private bool CanResetGame()
    {
        return CanResetDarts();
    }

    private bool IsResetOpenPalmActive()
    {
        if (GestureUtils.IsOpenPalm(trackedHand))
            return true;

        return gestureHandler != null && gestureHandler.GetGesture() == Gesture.OpenPalm;
    }

    private bool CanResetDarts()
    {
        if (Board != null && Board.GetComponent<BoardHandler>().points.Count > 0)
        {
            return true;
        }

        return GameObject.FindGameObjectsWithTag("Dart").Length > 0;
    }

    private void HandleDoubleGrabReset()
    {
        if (mode == Modes.Board || !CanCollectDroppedDarts())
        {
            grabCount = 0;
            grabWasActiveLastFrame = false;
            return;
        }

        bool grabIsActive = IsGrabResetActive();

        if (!grabIsActive && grabCount > 0 && Time.time - lastGrabTime > doubleGrabMaxDelay)
        {
            grabCount = 0;

            if (!resetGestureActive)
                stabilityFeedbackMessage = string.Empty;
        }

        // Detect only the moment the user STARTS a grab
        if (grabIsActive && !grabWasActiveLastFrame)
        {
            float timeSinceLastGrab = Time.time - lastGrabTime;

            if (timeSinceLastGrab <= doubleGrabMaxDelay)
            {
                grabCount++;
            }
            else
            {
                grabCount = 1;
            }

            lastGrabTime = Time.time;
            Debug.Log($"Double grab progress: {grabCount}");

            if (grabCount == 1 && !resetGestureActive)
                stabilityFeedbackMessage = "Close fist again to collect floor darts";

            if (grabCount >= 2)
            {
                TriggerCollectDroppedDarts();
                grabCount = 0;
            }
        }

        grabWasActiveLastFrame = grabIsActive;
    }

    private bool IsGrabResetActive()
    {
        if (GestureUtils.IsGrabbing(trackedHand))
            return true;

        return gestureHandler != null && gestureHandler.GetGesture() == Gesture.Grab;
    }

    private bool CanCollectDroppedDarts()
    {
        GameObject[] darts = GameObject.FindGameObjectsWithTag("Dart");
        foreach (GameObject dart in darts)
        {
            DartHandler dartHandler = dart.GetComponent<DartHandler>();
            if (dartHandler == null)
                continue;

            if (dartHandler.HasRegisteredBoardHit())
                continue;

            if (!dartHandler.IsStopped())
                continue;

            return true;
        }

        return false;
    }

    private Modes idle_state(Modes mode)
    {
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
        Gesture gesture = gestureHandler.GetGesture();

        if (gesture == Gesture.ThumbsUp)
        {
            if (!TryGetTrackedPalmPose(out MixedRealityPose palmPose))
                return mode;

            if (!moveBoard(palmPose))
                return mode;

            Line.SetActive(false);

            if (!boardPlacementActive)
            {
                boardPlacementActive = true;
                openPalmHoldStart = Time.time;
                lastBoardPosition = palmPose.Position;
                lastBoardRotation = palmPose.Rotation;
                boardLockProgress = 0f;
                stabilityFeedbackMessage = "Hold thumbs up steady to lock board";
                return mode;
            }

            float palmMovement = Vector3.Distance(palmPose.Position, lastBoardPosition);
            float palmRotationDelta = Quaternion.Angle(palmPose.Rotation, lastBoardRotation);
            bool palmIsStable = palmMovement <= boardStillThreshold && palmRotationDelta <= boardRotationStillThreshold;

            if (palmIsStable)
            {
                boardLockProgress = Mathf.Clamp01(boardLockProgress + (Time.fixedDeltaTime / boardLockHoldTime));
            }
            else
            {
                boardLockProgress = Mathf.Clamp01(boardLockProgress - ((Time.fixedDeltaTime / boardLockHoldTime) * unstableHoldDecayMultiplier));
            }

            lastBoardPosition = palmPose.Position;
            lastBoardRotation = palmPose.Rotation;

            openPalmHoldStart = Time.time - (boardLockProgress * boardLockHoldTime);
            stabilityFeedbackMessage = palmIsStable
                ? $"Board lock: {Mathf.RoundToInt(boardLockProgress * 100f)}%"
                : $"Steady thumbs up to lock: {Mathf.RoundToInt(boardLockProgress * 100f)}%";

            if (boardLockProgress >= 1f)
            {
                boardPlacementActive = false;
                openPalmHoldStart = -1f;
                boardLockProgress = 1f;
                stabilityFeedbackMessage = string.Empty;
                Debug.Log("Board locked in place after stable hold");
                return Modes.Idle;
            }

            return mode;
        }

        if (boardPlacementActive && gesture != Gesture.ThumbsUp)
        {
            boardPlacementActive = false;
            openPalmHoldStart = -1f;
            boardLockProgress = 0f;
            stabilityFeedbackMessage = string.Empty;
        }

        Line.SetActive(false);
        return mode;
    }

    private Modes dart_state(Modes mode)
    {
        Gesture currentGesture = gestureHandler.GetGesture();

        if (currentGesture == Gesture.Pinch)
        {
            lastPinchSeenTime = Time.time;
            trackSpeed();
            moveDart();
            Line.SetActive(true);
            return mode;
        }

        if (Time.time - lastPinchSeenTime < releaseConfirmTime)
        {
            moveDart();
            Line.SetActive(true);
            return mode;
        }

        if (track.Count < minThrowSamples)
        {
            stabilityFeedbackMessage = "Hold pinch briefly before release";
            return mode;
        }

        throwDart();
        stabilityFeedbackMessage = string.Empty;
        Line.SetActive(true);
        return Modes.Idle;
    }

    private void generateDart()
    {
        track.Clear();
        lastPinchSeenTime = Time.time;
        Dart = (GameObject)Instantiate(DartPrefab, new Vector3(0, 0, 0), Quaternion.identity);
        Dart.GetComponent<DartHandler>().Pause(true);
    }

    private void trackSpeed()
    {
        float currentTime = Time.time;

        if (!TryGetPinchPoint(out Vector3 pinchPoint))
            return;

        track.Enqueue((pinchPoint, currentTime));

        float intervalSeconds = interval / 1000f;
        while (track.Count > 0 && track.Peek().Item2 < currentTime - intervalSeconds)
            track.Dequeue();
    }

    private void throwDart()
    {
        if (Dart == null)
            return;

        Vector3 forward = Dart.transform.forward.normalized;
        Vector3 handVelocity = EstimateReleaseVelocity();
        float handSpeed = handVelocity.magnitude;

        Vector3 handDirection = handSpeed > 0.0001f ? handVelocity / handSpeed : forward;
        if (Vector3.Dot(handDirection, forward) < 0f)
            handDirection = forward;

        Vector3 releaseDirection = Vector3.Slerp(handDirection, forward, throwForwardBias).normalized;

        float speedScale = speedFactor * Constants.GetComponent<ConstantsScript>().DartsSpeed;
        float throwSpeed = Mathf.Clamp(handSpeed * speedScale, minThrowSpeed, maxThrowSpeed);

        speed = releaseDirection * throwSpeed;

        Debug.Log(speed);

        DartHandler dart = Dart.GetComponent<DartHandler>();
        dart.SetVelocity(speed);
        dart.Pause(false);

        Dart.transform.parent = null;
        Dart = null;
        track.Clear();
    }

    private bool TryGetPinchPoint(out Vector3 pinchPoint)
    {
        pinchPoint = Vector3.zero;

        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.IndexTip, trackedHand, out MixedRealityPose indexTip))
            return false;

        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.ThumbTip, trackedHand, out MixedRealityPose thumbTip))
            return false;

        pinchPoint = midpoint(indexTip.Position, thumbTip.Position, 0.5f);
        return true;
    }

    private Vector3 EstimateReleaseVelocity()
    {
        List<(Vector3, float)> points = new List<(Vector3, float)>(track);
        if (points.Count < 2)
            return Vector3.zero;

        Vector3 weightedVelocity = Vector3.zero;
        float totalWeight = 0f;

        for (int i = 0; i + 1 < points.Count; i++)
        {
            (Vector3, float) cur = points[i];
            (Vector3, float) next = points[i + 1];
            float dt = next.Item2 - cur.Item2;

            if (dt <= 0.0001f)
                continue;

            Vector3 segmentVelocity = (next.Item1 - cur.Item1) / dt;
            float ageWeight = (i + 1) / (float)(points.Count - 1);
            weightedVelocity += segmentVelocity * ageWeight;
            totalWeight += ageWeight;
        }

        if (totalWeight <= 0f)
            return Vector3.zero;

        return weightedVelocity / totalWeight;
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

    private bool TryGetTrackedPalmPose(out MixedRealityPose palmPose)
    {
        return HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, trackedHand, out palmPose);
    }

    private bool moveBoard(MixedRealityPose palmPose)
    {
        return Board.GetComponent<BoardHandler>().projectTo(palmPose.Position, palmPose.Forward);
    }
}