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

    private Vector3 speed = Vector3.zero;
    private Queue<(Vector3, float)> track = new Queue<(Vector3, float)>();

    [Header("Board Placement Settings")]
    [SerializeField]
    private float boardLockHoldTime = 2.0f;

    [SerializeField]
    private float boardStillThreshold = 0.03f;

    private bool boardPlacementActive = false;
    private float openPalmHoldStart = -1f;
    private Vector3 lastBoardPosition = Vector3.zero;

    [Header("Reset Gesture Settings")]
    [SerializeField]
    private GameObject UIControllerObject = null;

    [SerializeField]
    private float resetGestureCooldown = 1.5f;

    [SerializeField]
    private float doubleGrabMaxDelay = 0.8f;

    private float lastResetGestureTime = -10f;
    private float lastGrabTime = -10f;
    private int grabCount = 0;
    private bool grabWasActiveLastFrame = false;

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
        // Double-grab reset only outside board placement mode
        if (mode != Modes.Board)
        {
            HandleDoubleGrabReset();
        }
        else
        {
            // prevent stale grab state while placing board
            grabWasActiveLastFrame = false;
            grabCount = 0;
        }

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
        }
    }

    private void TriggerResetDarts()
    {
        if (Time.time - lastResetGestureTime < resetGestureCooldown)
            return;

        if (UIControllerObject != null)
        {
            UIControllerObject.GetComponent<UIControllerScript>().ResetGame();
            Debug.Log("Reset / collect darts triggered");
        }
        else
        {
            Debug.LogWarning("UIControllerObject is not assigned in InputController.");
        }

        lastResetGestureTime = Time.time;
    }

    private void HandleDoubleGrabReset()
    {
        Gesture currentGesture = gestureHandler.GetGesture();
        bool grabIsActive = (currentGesture == Gesture.Grab);

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

            if (grabCount >= 2)
            {
                TriggerResetDarts();
                grabCount = 0;
            }
        }

        grabWasActiveLastFrame = grabIsActive;
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

        if (gesture == Gesture.OpenPalm)
        {
            moveBoard();
            Line.SetActive(false);

            Vector3 currentBoardPosition = Board.transform.position;

            if (!boardPlacementActive)
            {
                boardPlacementActive = true;
                openPalmHoldStart = Time.time;
                lastBoardPosition = currentBoardPosition;
                return mode;
            }

            float boardMovement = Vector3.Distance(currentBoardPosition, lastBoardPosition);

            if (boardMovement > boardStillThreshold)
            {
                openPalmHoldStart = Time.time;
            }

            lastBoardPosition = currentBoardPosition;

            float heldTime = Time.time - openPalmHoldStart;

            if (heldTime >= boardLockHoldTime)
            {
                boardPlacementActive = false;
                openPalmHoldStart = -1f;
                Debug.Log("Board locked in place after stable hold");
                return Modes.Idle;
            }

            return mode;
        }

        if (boardPlacementActive && gesture != Gesture.OpenPalm)
        {
            boardPlacementActive = false;
            openPalmHoldStart = -1f;
        }

        Line.SetActive(false);
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
        Line.SetActive(true);

        return mode;
    }

    private void generateDart()
    {
        Dart = (GameObject)Instantiate(DartPrefab, new Vector3(0, 0, 0), Quaternion.identity);
        Dart.GetComponent<DartHandler>().Pause(true);
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
    }

    private void throwDart()
    {
        if (Dart == null)
            return;

        Vector3 dir = Vector3.zero;
        float magnitude = 0;
        List<(Vector3, float)> points = new List<(Vector3, float)>(track);

        if (points.Count >= 2)
        {
            for (int i = 0; (i + 1) < points.Count; i++)
            {
                (Vector3, float) cur = points[i];
                (Vector3, float) next = points[i + 1];
                Vector3 temp = (cur.Item1 - next.Item1) / (cur.Item2 - next.Item2);
                dir += temp;
                if (temp.magnitude > magnitude)
                    magnitude = temp.magnitude;
            }
        }

        speed = dir.normalized;
        speed *= magnitude;
        speed *= 1000f;
        speed *= speedFactor;
        speed *= Constants.GetComponent<ConstantsScript>().DartsSpeed;

        Debug.Log(speed);

        DartHandler dart = Dart.GetComponent<DartHandler>();
        dart.SetVelocity(speed);
        dart.Pause(false);

        Dart.transform.parent = null;
        Dart = null;
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

    private void moveBoard()
    {
        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, Handedness.Right, out MixedRealityPose jointPose))
            return;

        Board.GetComponent<BoardHandler>().projectTo(jointPose.Position, jointPose.Forward);
    }
}