using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;


public class DartHandler : MonoBehaviour
{
    [SerializeField]
    private GameObject Dart;

    [SerializeField]
    private GameObject Constants;

    [SerializeField]
    private TrailRenderer trail = null;

    [SerializeField]
    private Vector3 velocity = Vector3.zero;

    [SerializeField]
    private Vector3 defaultAcceleration = new Vector3(0, -6, 0);
    private Vector3 acceleration = new Vector3(0,-6, 0);

    private bool stopped = false;
    private bool boardHitRegistered = false;
    private int boardHitScore = 0;

    
    void Start()
    {

        List<GameObject> rootObjects = new List<GameObject>();
        Scene scene = SceneManager.GetActiveScene();

        scene.GetRootGameObjects(rootObjects);

        foreach (GameObject go in rootObjects)
        {
            if (go.name == "Constants")
            {
                Constants = go;
            }
        }
    }

    public void Pause(bool pause)
    {
        stopped = pause;
        trail.enabled = !pause;
    }

    public void SetVelocity(Vector3 velocity)
    {
        this.velocity = velocity;
        trail.enabled = true;
    }

    public bool HasRegisteredBoardHit()
    {
        return boardHitRegistered;
    }

    public int GetRegisteredBoardHitScore()
    {
        return boardHitScore;
    }

    public void ClearRegisteredBoardHit()
    {
        boardHitRegistered = false;
        boardHitScore = 0;
    }

    public bool IsStopped()
    {
        return stopped;
    }
    
    void Update()
    {
        if (!stopped)
        {

            Vector3 acc = Vector3.zero;
            acc += acceleration;
            acc *= Constants.GetComponent<ConstantsScript>().Gravity;
            velocity += acc * Time.deltaTime;

            int layerMaskCombined =
                  (1 << (int)Layers.UI)
                | (1 << (int)Layers.Menu)
                | (1 << (int)Layers.Dart)
                | (1 << (int)Layers.Gravity);

            layerMaskCombined = ~layerMaskCombined;
            Vector3 dir = velocity.normalized;
            Vector3 from = transform.position + dir * 0.05f;
            RaycastHit hit;
            if (Physics.Raycast(from, dir, out hit, (velocity * Time.deltaTime).magnitude, layerMaskCombined))
            {
                if (hit.collider.gameObject.name != "Dartboard")
                    Debug.Log(hit.collider.gameObject.name);
                HandleImpact(hit.collider, hit.point, dir);
            } else
            {
                transform.position += velocity * Time.deltaTime;
            }


            if (velocity.magnitude > 0.01)
                transform.forward = velocity.normalized;
        }
    }

    void OnTriggerEnter(Collider collision)
    {
        if (stopped && boardHitRegistered)
            return;

        Debug.Log(collision.tag);
        if (collision.tag == "Menu")
            return;

        if (collision.tag == "Gravity")
        {
            acceleration = collision.gameObject.GetComponent<GravityField>().getGravity();
            return;
        }

        Vector3 impactDir = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : transform.forward;
        HandleImpact(collision, transform.position, impactDir);
    }

    private void HandleImpact(Collider collision, Vector3 hitPoint, Vector3 hitDir)
    {
        transform.position = hitPoint;

        bool isBoardHit = collision.CompareTag("Board");
        BoardHandler board = collision.GetComponent<BoardHandler>();
        if (board == null)
            board = collision.GetComponentInParent<BoardHandler>();

        if (board != null)
            isBoardHit = true;

        if (board != null)
        {
            transform.position += hitDir.normalized * 0.007f;
            transform.parent = board.transform;

            if (!boardHitRegistered)
            {
                boardHitScore = board.hit(Dart);
                boardHitRegistered = true;
            }
        }

        velocity = Vector3.zero;
        stopped = true;
    }
}