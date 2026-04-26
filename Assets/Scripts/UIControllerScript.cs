using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UIControllerScript : MonoBehaviour
{
    public GameObject InputController;
    public GameObject Board;
    public GameObject Scoreboard;
    public GameObject AnalyticsPanel;
    public GameObject InstantFeedbackText;

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

    [SerializeField]
    private int recentThrowsShown = 6;
    [SerializeField]
    private float instantFeedbackVisibleSeconds = 1.35f;
    [SerializeField]
    private float instantFeedbackFadeSeconds = 0.35f;

    private TextMeshProUGUI scoreboardText = null;
    private TextMeshProUGUI analyticsText = null;
    private TextMeshProUGUI instantFeedbackText = null;
    private string activeInstantFeedback = string.Empty;
    private Color activeInstantFeedbackColor = Color.white;
    private float activeInstantFeedbackUntil = -1f;
    private float lastConsumedInstantFeedbackTime = -1f;

    void Start()
    {
        if (Scoreboard != null)
            scoreboardText = Scoreboard.GetComponent<TextMeshProUGUI>();

        if (AnalyticsPanel != null)
            analyticsText = AnalyticsPanel.GetComponent<TextMeshProUGUI>();

        if (InstantFeedbackText != null)
            instantFeedbackText = InstantFeedbackText.GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        if (Board == null || scoreboardText == null)
            return;

        BoardHandler boardHandler = Board.GetComponent<BoardHandler>();
        if (boardHandler == null)
            return;

        float stabilityScore = 0f;
        if (InputController != null)
        {
            InputController input = InputController.GetComponent<InputController>();
            if (input != null)
                stabilityScore = input.GetCurrentStabilityScore();
        }

        SyncInstantFeedback(boardHandler);
        string inlineFeedback = BuildInlineFeedbackText();
        scoreboardText.text =
            inlineFeedback +
            "\r\nPOINTS: \r\n" + boardHandler.GetPoints() +
            "\r\n\r\nDARTS HIT: \r\n" + boardHandler.GetHitCount() +
            "\r\n\r\nSTABILITY: \r\n" + stabilityScore.ToString("F2") +
            "\r\n\r\n" + boardHandler.LastThrowSummary;

        string panelText = boardHandler.GetAnalyticsPanelText(recentThrowsShown);
        if (analyticsText != null)
            analyticsText.text = panelText;
        else
            scoreboardText.text += "\n\n" + panelText;
    }

    private void SyncInstantFeedback(BoardHandler boardHandler)
    {
        if (boardHandler.LastInstantFeedbackTime > lastConsumedInstantFeedbackTime)
        {
            lastConsumedInstantFeedbackTime = boardHandler.LastInstantFeedbackTime;
            activeInstantFeedback = boardHandler.LastInstantFeedback;
            activeInstantFeedbackColor = boardHandler.LastInstantFeedbackColor;
            activeInstantFeedbackUntil = Time.time + Mathf.Max(0.1f, instantFeedbackVisibleSeconds);
        }

        if (instantFeedbackText == null)
            return;

        if (!HasActiveInstantFeedback())
        {
            instantFeedbackText.text = string.Empty;
            return;
        }

        Color color = activeInstantFeedbackColor;
        color.a = GetInstantFeedbackAlpha();
        instantFeedbackText.text = activeInstantFeedback;
        instantFeedbackText.color = color;
    }

    private bool HasActiveInstantFeedback()
    {
        return !string.IsNullOrEmpty(activeInstantFeedback) && Time.time <= activeInstantFeedbackUntil;
    }

    private float GetInstantFeedbackAlpha()
    {
        float fade = Mathf.Max(0.001f, instantFeedbackFadeSeconds);
        float fadeStart = activeInstantFeedbackUntil - fade;
        if (Time.time <= fadeStart)
            return 1f;

        return Mathf.Clamp01((activeInstantFeedbackUntil - Time.time) / fade);
    }

    private string BuildInlineFeedbackText()
    {
        if (instantFeedbackText != null || !HasActiveInstantFeedback())
            return string.Empty;

        Color color = activeInstantFeedbackColor;
        color.a = GetInstantFeedbackAlpha();
        string hex = ColorUtility.ToHtmlStringRGBA(color);
        return $"<color=#{hex}>FEEDBACK: {activeInstantFeedback}</color>\r\n\r\n";
    }

    public void SetGravity(SliderEventData eventData)
    {
        float newValue = GravityMultiplier * eventData.NewValue;

        if (GravityDisplay != null)
            GravityDisplay.GetComponent<TextMeshPro>().text = $"{newValue:F2}";

        if (Constants != null)
            Constants.GetComponent<ConstantsScript>().Gravity = newValue;
    }

    public void SetSpeed(SliderEventData eventData)
    {
        float newValue = SpeedMultiplier * eventData.NewValue;

        if (SpeedDisplay != null)
            SpeedDisplay.GetComponent<TextMeshPro>().text = $"{newValue:F2}";

        if (Constants != null)
            Constants.GetComponent<ConstantsScript>().DartsSpeed = newValue;
    }

    public void ToggleSliderActive()
    {
        if (GravitySlider == null || SpeedSlider == null)
            return;

        bool shouldEnable = !GravitySlider.activeSelf;
        GravitySlider.SetActive(shouldEnable);
        SpeedSlider.SetActive(shouldEnable);
    }

    public void SetBoardState()
    {
        if (InputController == null)
            return;

        Debug.Log("Switched to Board state! (Reason: Board placement button pressed)");
        InputController.GetComponent<InputController>().SetGameState(Modes.Board);
    }

    public void ChangeLevel()
    {
        if (Levels == null || Levels.Count == 0)
            return;

        currentLevel = (currentLevel + 1) % Levels.Count;

        for (int i = 0; i < Levels.Count; i++)
            Levels[i].SetActive(i == currentLevel);

        if (levelButton != null)
        {
            ButtonConfigHelper helper = levelButton.GetComponent<ButtonConfigHelper>();
            if (helper != null)
                helper.MainLabelText = $"Change level\r\nCurrent level: {currentLevel}";
        }
    }

    public int GetNumberOfHitDarts()
    {
        if (Board == null)
            return 0;

        BoardHandler boardHandler = Board.GetComponent<BoardHandler>();
        if (boardHandler == null)
            return 0;

        return boardHandler.GetHitCount();
    }

    public void ResetGame()
    {
        if (Board != null)
        {
            BoardHandler boardHandler = Board.GetComponent<BoardHandler>();
            if (boardHandler != null)
                boardHandler.ResetPoints();
        }

        List<GameObject> rootObjects = new List<GameObject>();
        Scene scene = SceneManager.GetActiveScene();
        scene.GetRootGameObjects(rootObjects);

        for (int i = 0; i < rootObjects.Count; i++)
        {
            GameObject go = rootObjects[i];
            if (go.tag == "Dart")
                Destroy(go);
        }

        if (Board != null)
        {
            foreach (Transform child in Board.transform)
            {
                if (child.gameObject.tag == "Dart")
                    Destroy(child.gameObject);
            }
        }
    }
}
