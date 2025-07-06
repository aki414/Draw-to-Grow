using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class Pen : MonoBehaviour
{
    [Header("Pen Properties")]
    public Transform tip;
    public Material drawingMaterial;
    public Material tipMaterial;
    [Range(0.01f, 0.1f)]
    public float penWidth = 0.05f;
    public Color[] penColors;

    [Header("XR Interaction")]
    public XRGrabInteractable grabInteractable;

    [Header("Drawing Settings")]
    public float minDrawDistance = 0.01f;
    public int maxPointsPerLine = 1000;

    [Header("Input Actions")]
    public InputActionReference leftTriggerAction;
    public InputActionReference rightTriggerAction;
    public InputActionReference colorSwitchAction;

    private LineRenderer currentDrawing;
    private int currentColorIndex;
    private bool wasDrawing = false;
    private Vector3 lastDrawPosition;
    private XRBaseInteractor currentInteractor;

    void Start()
    {
        currentColorIndex = 0;
        if (tipMaterial != null && penColors != null && penColors.Length > 0)
        {
            tipMaterial.color = penColors[currentColorIndex];
        }

        // Subscribe to grab events
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnGrabbed);
            grabInteractable.selectExited.AddListener(OnReleased);
        }

        // Enable input actions
        EnableInputActions();
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        // Disable input actions
        DisableInputActions();
    }

    void Update()
    {
        HandleDrawing();
        HandleColorSwitch();
    }

    private void EnableInputActions()
    {
        if (leftTriggerAction != null)
            leftTriggerAction.action.Enable();
        if (rightTriggerAction != null)
            rightTriggerAction.action.Enable();
        if (colorSwitchAction != null)
            colorSwitchAction.action.Enable();
    }

    private void DisableInputActions()
    {
        if (leftTriggerAction != null)
            leftTriggerAction.action.Disable();
        if (rightTriggerAction != null)
            rightTriggerAction.action.Disable();
        if (colorSwitchAction != null)
            colorSwitchAction.action.Disable();
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        currentInteractor = args.interactorObject as XRBaseInteractor;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        currentInteractor = null;
        if (wasDrawing)
        {
            EndDrawing();
        }
    }

    private bool IsTriggerPressed()
    {
        if (currentInteractor == null) return false;

        // Check which hand is grabbing and get corresponding trigger input
        bool leftTrigger = leftTriggerAction != null && leftTriggerAction.action.ReadValue<float>() > 0.5f;
        bool rightTrigger = rightTriggerAction != null && rightTriggerAction.action.ReadValue<float>() > 0.5f;

        // You can also check by interactor name or tag if needed
        string interactorName = currentInteractor.name.ToLower();
        if (interactorName.Contains("left"))
            return leftTrigger;
        else if (interactorName.Contains("right"))
            return rightTrigger;

        // Default: return true if either trigger is pressed
        return leftTrigger || rightTrigger;
    }

    private void HandleDrawing()
    {
        bool isGrabbed = grabInteractable != null && grabInteractable.isSelected;
        bool shouldDraw = isGrabbed && IsTriggerPressed();

        if (shouldDraw)
        {
            Draw();
        }
        else
        {
            if (wasDrawing)
            {
                EndDrawing();
            }
        }

        wasDrawing = shouldDraw;
    }

    private void Draw()
    {
        if (tip == null) return;

        Vector3 currentTipPosition = tip.position;

        // Start new line if needed
        if (currentDrawing == null)
        {
            StartNewLine(currentTipPosition);
            return;
        }

        // Check if we should add a new point
        if (Vector3.Distance(lastDrawPosition, currentTipPosition) > minDrawDistance)
        {
            AddPointToLine(currentTipPosition);
        }
    }

    private void StartNewLine(Vector3 startPosition)
    {
        // Create new GameObject for the line
        GameObject lineObj = new GameObject("DrawingLine");
        currentDrawing = lineObj.AddComponent<LineRenderer>();

        // Configure LineRenderer
        currentDrawing.material = drawingMaterial;
        currentDrawing.startColor = currentDrawing.endColor = penColors[currentColorIndex];
        currentDrawing.startWidth = currentDrawing.endWidth = penWidth;
        currentDrawing.positionCount = 1;
        currentDrawing.useWorldSpace = true;
        currentDrawing.SetPosition(0, startPosition);

        lastDrawPosition = startPosition;
    }

    private void AddPointToLine(Vector3 newPosition)
    {
        if (currentDrawing.positionCount >= maxPointsPerLine)
        {
            // Start a new line if we've reached max points
            EndDrawing();
            StartNewLine(newPosition);
            return;
        }

        // Add new point to existing line
        int newIndex = currentDrawing.positionCount;
        currentDrawing.positionCount = newIndex + 1;
        currentDrawing.SetPosition(newIndex, newPosition);

        lastDrawPosition = newPosition;
    }

    private void EndDrawing()
    {
        currentDrawing = null;
    }

    private void HandleColorSwitch()
    {
        if (colorSwitchAction != null && colorSwitchAction.action.WasPressedThisFrame())
        {
            SwitchColor();
        }
    }

    private void SwitchColor()
    {
        if (penColors == null || penColors.Length == 0) return;

        currentColorIndex = (currentColorIndex + 1) % penColors.Length;

        if (tipMaterial != null)
        {
            tipMaterial.color = penColors[currentColorIndex];
        }
    }

    // Public methods for external control
    public void SetColor(int colorIndex)
    {
        if (penColors != null && colorIndex >= 0 && colorIndex < penColors.Length)
        {
            currentColorIndex = colorIndex;
            if (tipMaterial != null)
            {
                tipMaterial.color = penColors[currentColorIndex];
            }
        }
    }

    public void ClearCurrentDrawing()
    {
        if (currentDrawing != null)
        {
            DestroyImmediate(currentDrawing.gameObject);
            currentDrawing = null;
        }
    }

    public Color GetCurrentColor()
    {
        if (penColors != null && currentColorIndex < penColors.Length)
        {
            return penColors[currentColorIndex];
        }
        return Color.white;
    }

    // Alternative input method without Input Actions (if you prefer direct input)
    private bool IsTriggerPressedDirect()
    {
        if (currentInteractor == null) return false;

        // This is a fallback method using Unity's Input system directly
        // You can uncomment this if you prefer not to use Input Actions
        /*
        if (currentInteractor.name.ToLower().Contains("left"))
            return Input.GetAxis("XRI_Left_Trigger") > 0.5f;
        else if (currentInteractor.name.ToLower().Contains("right"))
            return Input.GetAxis("XRI_Right_Trigger") > 0.5f;
        */

        return false;
    }
}