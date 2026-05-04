using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Microsoft.MixedReality.Toolkit.UI;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using TMPro;
using static Microsoft.MixedReality.Toolkit.Experimental.UI.KeyboardKeyFunc;
using UnityEngine.PlayerLoop;
using UnityEngine.Serialization;
using Microsoft;
//using Microsoft.MixedReality.OpenXR;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using Microsoft.MixedReality.Toolkit.WindowsMixedReality;

public class UIControllerScript : MonoBehaviour
{

    [SerializeField]
    private GameObject MenuPanelsRoot;

    public GameObject InputController;
    public GameObject Board; //used to obtain current points
    public GameObject Scoreboard; //display score text

    //these two objects serve as toggle for the placement mode and the reset button
    //public GameObject PlacementToggleObject;
    //public GameObject ResetToggleObject;

    public GameObject GravitySlider;
    public GameObject SpeedSlider;

    public GameObject GravityDisplay;
    public GameObject SpeedDisplay;

    public float GravityMultiplier;
    public float SpeedMultiplier;

    public GameObject Constants;

    public List<GameObject> Levels;

    public int currentLevel = 0;

    public GameObject levelButton;

    // Start is called before the first frame update
    void Start()
    {
        if (MenuPanelsRoot == null)
        {
            Transform infoPanels = transform.Find("InfoPanels");
            if (infoPanels != null)
                MenuPanelsRoot = infoPanels.gameObject;
        }
    }

    // Update is called once per frame
    void Update()
    {
        //periodically update the UI scoreboard
        //ScoreBoard.GetComponent<ButtonConfigHelper>().MainLabelText = $"Score: {Board.GetComponent<BoardHandler>().GetPoints()}"; //old version, where scoreboard was on the menu
        //Debug.Log(Scoreboard.GetComponent<TextMeshProUGUI>().text);

        int score = Board.GetComponent<BoardHandler>().GetPoints();
        int num_darts = GetNumberOfHitDarts();
        string feedback = string.Empty;
        if (InputController != null)
        {
            feedback = InputController.GetComponent<InputController>().GetStabilityFeedbackMessage();
        }

        string feedbackSection = string.IsNullOrEmpty(feedback) ? string.Empty : $"\r\n\r\nSTATUS: \r\n{feedback}";
        Scoreboard.GetComponent<TextMeshProUGUI>().text = $"\r\nPOINTS: \r\n{score}\r\n\r\nDARTS HIT: \r\n{num_darts}{feedbackSection}";

    }

    public void SetGravity(SliderEventData eventData)
    {
        float newValue = GravityMultiplier * eventData.NewValue;

        GravityDisplay.GetComponent<TextMeshPro>().text = $"{newValue:F2}";
        Constants.GetComponent<ConstantsScript>().Gravity = newValue;
    }

    public void SetSpeed(SliderEventData eventData)
    {

        float newValue = SpeedMultiplier * eventData.NewValue;
        SpeedDisplay.GetComponent<TextMeshPro>().text = $"{newValue:F2}";
        Constants.GetComponent<ConstantsScript>().DartsSpeed = newValue;
    }

    public void ToggleSliderActive()
    {
        if (GravitySlider.activeSelf)
        {
            GravitySlider.SetActive(false);
            SpeedSlider.SetActive(false);
        }
        else
        {
            GravitySlider.SetActive(true);
            SpeedSlider.SetActive(true);
        }
    }

    public void ToggleMenuVisibility()
    {
        if (MenuPanelsRoot == null)
        {
            Debug.LogWarning("MenuPanelsRoot is not assigned on UIControllerScript.");
            return;
        }

        bool shouldShow = !MenuPanelsRoot.activeSelf;
        MenuPanelsRoot.SetActive(shouldShow);
        Debug.Log($"Menu {(shouldShow ? "opened" : "closed")} by peace gesture");
    }

    public bool IsMenuVisible()
    {
        return MenuPanelsRoot != null && MenuPanelsRoot.activeSelf;
    }

    //when pressing the "place board" button, enable board placement
    public void SetBoardState()
    {
        Debug.Log("Switched to Board state! (Reason: Board placement button pressed)");
        InputController.GetComponent<InputController>().SetGameState(Modes.Board);
    }


    //select which gamemode is currently being played (for the future)
    void SetGameMode()
    {
        //doesn't do anything yet
    }

    public void ChangeLevel()
    {
        currentLevel = (currentLevel + 1) % Levels.Count;

        for (int i = 0; i < Levels.Count; i++)
        {
            if (i == currentLevel)
            {
                Levels[i].SetActive(true);
            }
            else
            {
                Levels[i].SetActive(false);
            }
        }

        levelButton.GetComponent<ButtonConfigHelper>().MainLabelText = $"Change level\r\nCurrent level: {currentLevel}";

    }

    public int GetNumberOfHitDarts()
    {
        int darts_counter = 0;
        /*foreach (Transform child in Board.transform)
        {
            if (child.gameObject.tag == "Dart")
            {
                darts_counter++;
            }
        }*/

        foreach (int elem in Board.GetComponent<BoardHandler>().points)
        {
            if (elem != 0)
            { 
                darts_counter++;
            }
        }

        return darts_counter;
    }

    public bool ResetSingleDart()
    {
        GameObject[] darts = GameObject.FindGameObjectsWithTag("Dart");

        if (darts.Length == 0)
        {
            Debug.Log("Single dart reset found no dart objects.");
            return false;
        }

        GameObject dartToRemove = FindNextDartToRemove(darts);
        if (dartToRemove == null)
        {
            Debug.LogWarning("Single dart reset could not choose a dart to remove.");
            return false;
        }

        DartHandler dartHandler = dartToRemove.GetComponent<DartHandler>();
        if (dartHandler != null && dartHandler.HasRegisteredBoardHit())
        {
            Board.GetComponent<BoardHandler>().RemoveHitPoints(dartHandler.GetRegisteredBoardHitScore());
        }

        Destroy(dartToRemove);
        Debug.Log($"Removed one dart: {dartToRemove.name}");
        return true;
    }

    public int CollectDroppedDarts(int maxCount)
    {
        GameObject[] darts = GameObject.FindGameObjectsWithTag("Dart");
        List<GameObject> droppedDarts = new List<GameObject>();

        foreach (GameObject dart in darts)
        {
            DartHandler dartHandler = dart.GetComponent<DartHandler>();
            if (dartHandler == null)
                continue;

            if (dartHandler.HasRegisteredBoardHit())
                continue;

            if (!dartHandler.IsStopped())
                continue;

            droppedDarts.Add(dart);
        }

        int collectedCount = Mathf.Min(maxCount, droppedDarts.Count);
        for (int index = 0; index < collectedCount; index++)
        {
            Destroy(droppedDarts[index]);
        }

        Debug.Log($"Collected {collectedCount} dropped dart(s)");
        return collectedCount;
    }

    public bool ResetScoreOnly()
    {
        BoardHandler boardHandler = Board.GetComponent<BoardHandler>();
        if (boardHandler == null)
            return false;

        if (boardHandler.points.Count == 0)
        {
            Debug.Log("Score reset found no board score to clear.");
            return false;
        }

        boardHandler.ResetPoints();

        GameObject[] darts = GameObject.FindGameObjectsWithTag("Dart");
        foreach (GameObject dart in darts)
        {
            DartHandler dartHandler = dart.GetComponent<DartHandler>();
            if (dartHandler != null)
                dartHandler.ClearRegisteredBoardHit();
        }

        Debug.Log("Score reset cleared board points without removing darts.");
        return true;
    }

    private GameObject FindNextDartToRemove(GameObject[] darts)
    {
        foreach (GameObject dart in darts)
        {
            DartHandler dartHandler = dart.GetComponent<DartHandler>();
            if (dartHandler != null && dartHandler.HasRegisteredBoardHit())
                return dart;
        }

        return darts[0];
    }

    //set the counter on the board to zero and destory all Dart game objects
    public void ResetGame()
    {
        Board.GetComponent<BoardHandler>().ResetPoints();

        GameObject[] darts = GameObject.FindGameObjectsWithTag("Dart");

        foreach (GameObject dart in darts)
        {
            Destroy(dart);
        }

        Debug.Log($"Reset destroyed {darts.Length} dart(s)");


    }



}