using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GestureUtils
{
    private const float PinchThresholdPlacement = 0.7f;
    private const float PinchThresholdThrowing = 0.2f;
    private const float GrabThreshold = 0.4f;

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

    public static bool IsOpenPalm(Handedness trackedHand)
    {
        if (HandJointUtils.FindHand(trackedHand) is null)
            return false;

        if (!HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, trackedHand, out MixedRealityPose palm))
            return false;

        float indexCurl = HandPoseUtils.IndexFingerCurl(trackedHand);
        float middleCurl = HandPoseUtils.MiddleFingerCurl(trackedHand);
        float ringCurl = HandPoseUtils.RingFingerCurl(trackedHand);
        float pinkyCurl = HandPoseUtils.PinkyFingerCurl(trackedHand);
        float thumbCurl = HandPoseUtils.ThumbFingerCurl(trackedHand);

        bool fingersOpen =
            indexCurl < 0.35f &&
            middleCurl < 0.35f &&
            ringCurl < 0.35f &&
            pinkyCurl < 0.35f &&
            thumbCurl < 0.65f;

        bool palmFacingForward =
            Vector3.Dot(palm.Forward, Camera.main.transform.forward) > 0.2f ||
            Vector3.Dot(palm.Up, Vector3.up) > 0.0f;

        return fingersOpen && palmFacingForward;
    }
}