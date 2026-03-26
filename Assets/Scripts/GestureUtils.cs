using Microsoft.MixedReality.Toolkit.Utilities;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GestureUtils
{
    private const float PinchThresholdPlacement = 0.7f;
    private const float PinchThresholdThrowing = 0.2f;
    private const float GrabThreshold = 0.4f;
    private const float OpenPalmCurlThreshold = 0.4f;

    public static bool IsPinching(Handedness trackedHand)
    {
        List<GameObject> rootObjects = new List<GameObject>();
        Scene scene = SceneManager.GetActiveScene();

        scene.GetRootGameObjects(rootObjects);

        GameObject inputControllerObject = null;

        foreach (GameObject go in rootObjects)
        {
            if (go.name == "Constants")
            {
                inputControllerObject = go.GetComponent<ConstantsScript>().InputController;
            }
        }

        float currentThreshold = PinchThresholdPlacement;

        if (inputControllerObject != null)
        {
            InputController inputController = inputControllerObject.GetComponent<InputController>();
            if (inputController != null &&
                (inputController.mode == Modes.Dart || inputController.mode == Modes.Idle))
            {
                currentThreshold = PinchThresholdThrowing;
            }
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
        return !IsPinching(trackedHand) &&
               !IsGrabbing(trackedHand) &&
               HandPoseUtils.IndexFingerCurl(trackedHand) < OpenPalmCurlThreshold &&
               HandPoseUtils.MiddleFingerCurl(trackedHand) < OpenPalmCurlThreshold &&
               HandPoseUtils.RingFingerCurl(trackedHand) < OpenPalmCurlThreshold &&
               HandPoseUtils.PinkyFingerCurl(trackedHand) < OpenPalmCurlThreshold &&
               HandPoseUtils.ThumbFingerCurl(trackedHand) < OpenPalmCurlThreshold;
    }
}