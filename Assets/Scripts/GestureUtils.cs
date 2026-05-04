using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GestureUtils
{
    private const float PinchThresholdPlacement = 0.7f;
    private const float PinchThresholdThrowing = 0.2f;
    private const float GrabThreshold = 0.28f;
    private const float OpenFingerThreshold = 0.35f;
    private const float ClosedFingerThreshold = 0.55f;

    public static bool IsPinching(Handedness trackedHand)
    {
        List<GameObject> rootObjects = new List<GameObject>();
        Scene scene = SceneManager.GetActiveScene();

        scene.GetRootGameObjects(rootObjects);

        GameObject InputController = null;

        foreach (GameObject go in rootObjects)
        {
            if (go.name == "Constants")
            {
                InputController = go.GetComponent<ConstantsScript>().InputController;
            }
        }

        float currentThreshold = PinchThresholdPlacement;
        if (InputController.GetComponent<InputController>().mode == Modes.Dart ||
            InputController.GetComponent<InputController>().mode == Modes.Idle)
        {
            currentThreshold = PinchThresholdThrowing;
        }

        return HandPoseUtils.CalculateIndexPinch(trackedHand) > currentThreshold;
    }

    public static bool IsGrabbing(Handedness trackedHand)
    {
        return !IsPinching(trackedHand) &&
               HandPoseUtils.MiddleFingerCurl(trackedHand) > GrabThreshold &&
               HandPoseUtils.RingFingerCurl(trackedHand) > GrabThreshold &&
               HandPoseUtils.PinkyFingerCurl(trackedHand) > GrabThreshold &&
               HandPoseUtils.ThumbFingerCurl(trackedHand) > GrabThreshold;
    }

    public static bool IsPeaceSign(Handedness trackedHand)
    {
        if (HandJointUtils.FindHand(trackedHand) is null)
            return false;

        float indexCurl = HandPoseUtils.IndexFingerCurl(trackedHand);
        float middleCurl = HandPoseUtils.MiddleFingerCurl(trackedHand);
        float ringCurl = HandPoseUtils.RingFingerCurl(trackedHand);
        float pinkyCurl = HandPoseUtils.PinkyFingerCurl(trackedHand);
        float thumbCurl = HandPoseUtils.ThumbFingerCurl(trackedHand);

        bool indexAndMiddleOpen =
            indexCurl < OpenFingerThreshold &&
            middleCurl < OpenFingerThreshold;

        bool ringAndPinkyClosed =
            ringCurl > ClosedFingerThreshold &&
            pinkyCurl > ClosedFingerThreshold;

        bool thumbRelaxed = thumbCurl < 0.85f;

        return !IsPinching(trackedHand) && indexAndMiddleOpen && ringAndPinkyClosed && thumbRelaxed;
    }

    public static bool IsOpenPalm(Handedness trackedHand)
    {
        if (HandJointUtils.FindHand(trackedHand) is null)
            return false;

        float indexCurl = HandPoseUtils.IndexFingerCurl(trackedHand);
        float middleCurl = HandPoseUtils.MiddleFingerCurl(trackedHand);
        float ringCurl = HandPoseUtils.RingFingerCurl(trackedHand);
        float pinkyCurl = HandPoseUtils.PinkyFingerCurl(trackedHand);
        float thumbCurl = HandPoseUtils.ThumbFingerCurl(trackedHand);

        bool fingersOpen =
            indexCurl < 0.78f &&
            middleCurl < 0.78f &&
            ringCurl < 0.78f &&
            pinkyCurl < 0.78f &&
            thumbCurl < 0.95f;

        return fingersOpen && !IsPinching(trackedHand) && !IsGrabbing(trackedHand) && !IsPeaceSign(trackedHand);
    }

    public static bool IsThumbsUp(Handedness trackedHand)
    {
    if (HandJointUtils.FindHand(trackedHand) is null)
        return false;

    if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.ThumbTip, trackedHand, out MixedRealityPose thumb))
        return false;

    float indexCurl = HandPoseUtils.IndexFingerCurl(trackedHand);
    float middleCurl = HandPoseUtils.MiddleFingerCurl(trackedHand);
    float ringCurl = HandPoseUtils.RingFingerCurl(trackedHand);
    float pinkyCurl = HandPoseUtils.PinkyFingerCurl(trackedHand);

    bool fingersClosed =
        indexCurl > 0.4f &&
        middleCurl > 0.4f &&
        ringCurl > 0.4f &&
        pinkyCurl > 0.4f;

    bool thumbUp =
        Vector3.Dot(thumb.Up, Vector3.up) > 0.2f;

    return fingersClosed && thumbUp;
    }
}