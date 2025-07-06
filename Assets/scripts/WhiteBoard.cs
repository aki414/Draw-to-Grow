using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class WhiteBoard : MonoBehaviour
{
    [Tooltip("The Render Texture to draw on")]
    public RenderTexture renderTexture;

    [Header("Brush Settings")]
    [Tooltip("Max distance for brush detection")]
    public float maxDistance = 0.2f;
    [Tooltip("Minimum distance between brush positions")]
    public float minBrushDistance = 2f;

    private Material brushMaterial; // Material used for GL drawing

    public Color backgroundColor = Color.white;
    [Range(0, 1)]
    public float markerAlpha = 0.7f;

    [Header("Collider Type")]
    [Tooltip("Set false to use a MeshCollider for 3d objects")]
    public bool useBoxCollider = true;

    // Define a Brush class to hold properties for each brush
    [System.Serializable]
    public class BrushSettings
    {
        [Header("XR Interaction")]
        public XRGrabInteractable brushGrabbable; // XR Grabbable component
        public Transform brushTransform; // Transform of the brush tip

        [Header("Brush Properties")]
        public Color color = Color.black; // Brush color
        public int sizeY = 20; // Brush size in pixels
        public int sizeX = 20; // Brush size in pixels
        public bool isEraser = false;

        [Header("Runtime State")]
        [HideInInspector] public Vector2 lastPosition; // Last drawn position
        [HideInInspector] public bool isFirstDraw = true; // Flag for first draw
        [HideInInspector] public bool isDrawing = false; // Whether the brush is in contact
        [HideInInspector] public bool isGrabbed = false; // Whether the brush is being held
    }

    [Header("Add Brushes")]
    public List<BrushSettings> brushes = new List<BrushSettings>(); // List to hold multiple brushes

    private void Start()
    {
        InitializeBrushMaterial();
        SetupRenderTexture();
        InitializeBrushes();
    }

    private void InitializeBrushMaterial()
    {
        // Initialize the brush material with a simple shader
        brushMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
        if (brushMaterial == null)
        {
            Debug.LogError("Failed to create brush material! Make sure the shader exists.");
            return;
        }
    }

    private void SetupRenderTexture()
    {
        if (renderTexture == null)
        {
            Debug.LogError("Render Texture is not assigned!");
            return;
        }

        // Set the Render Texture as the main texture of the object's material
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.mainTexture = renderTexture;
        }

        // Clear the Render Texture at the start
        RenderTexture.active = renderTexture;
        GL.Clear(true, true, backgroundColor);
        RenderTexture.active = null;
    }

    private void InitializeBrushes()
    {
        foreach (BrushSettings brush in brushes)
        {
            if (brush.brushGrabbable != null)
            {
                // Subscribe to grab events
                brush.brushGrabbable.selectEntered.AddListener((args) => OnBrushGrabbed(brush, args));
                brush.brushGrabbable.selectExited.AddListener((args) => OnBrushReleased(brush, args));
            }

            // Set the alpha level of the markers
            brush.color.a = markerAlpha;

            // Set eraser color to background color
            if (brush.isEraser)
            {
                brush.color = backgroundColor;
                brush.color.a = 1f; // Erasers should be fully opaque
            }
        }
    }

    private void OnBrushGrabbed(BrushSettings brush, SelectEnterEventArgs args)
    {
        brush.isGrabbed = true;
        brush.isFirstDraw = true;
    }

    private void OnBrushReleased(BrushSettings brush, SelectExitEventArgs args)
    {
        brush.isGrabbed = false;
        brush.isDrawing = false;
        brush.isFirstDraw = true;
    }

    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        foreach (BrushSettings brush in brushes)
        {
            if (brush.brushGrabbable != null)
            {
                brush.brushGrabbable.selectEntered.RemoveAllListeners();
                brush.brushGrabbable.selectExited.RemoveAllListeners();
            }
        }
    }

    // Update method for drawing - keeping the same logic as original for marker effect
    public void Update()
    {
        if (renderTexture == null || brushMaterial == null) return;

        // Ensure the Render Texture is active for drawing
        RenderTexture.active = renderTexture;

        // Draw each brush on the texture
        foreach (var brush in brushes)
        {
            // Check if the brush is being held to only run functions for brushes being used
            if (brush.brushGrabbable != null && brush.isGrabbed)
            {
                DrawBrushOnTexture(brush);
            }
        }

        // Deactivate the Render Texture after drawing
        RenderTexture.active = null;
    }

    private void DrawBrushOnTexture(BrushSettings brush)
    {
        if (brush.brushTransform == null) return; // Null check

        // Raycast from the brush tip transform
        Ray ray = new Ray(brush.brushTransform.position, brush.brushTransform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
        {
            // Check if the raycast from the brush is hitting this game object (the board)
            if (hit.collider.gameObject == gameObject)
            {
                Vector2 uv = CalculateUVCoordinates(hit);

                // Convert UV coordinates to texture space
                int x = (int)(uv.x * renderTexture.width);
                int y = (int)(uv.y * renderTexture.height);
                Vector2 currentPosition = new Vector2(x, y);

                // Handle drawing state
                if (!brush.isDrawing)
                {
                    // Reset when the brush starts drawing again
                    brush.isFirstDraw = true;
                    brush.isDrawing = true;
                }

                if (brush.isFirstDraw)
                {
                    DrawAtPosition(currentPosition, brush.color, brush.sizeX, brush.sizeY, brush.brushTransform.rotation.eulerAngles.z);
                    brush.lastPosition = currentPosition;
                    brush.isFirstDraw = false;
                    return;
                }

                // Handle edge wrapping for 3D objects
                if (ShouldSkipInterpolation(currentPosition, brush.lastPosition))
                {
                    // If crossing an edge, do not interpolate. Just draw at the current position
                    DrawAtPosition(currentPosition, brush.color, brush.sizeX, brush.sizeY, brush.brushTransform.rotation.eulerAngles.z);
                }
                else
                {
                    // Interpolate between the last position and the current position
                    InterpolateAndDraw(brush.lastPosition, currentPosition, brush);
                }

                brush.lastPosition = currentPosition; // Update the last drawn position
            }
        }
        else
        {
            // Stop drawing when the brush is no longer in contact
            brush.isDrawing = false;
        }
    }

    private Vector2 CalculateUVCoordinates(RaycastHit hit)
    {
        Vector2 uv;

        if (useBoxCollider)
        {
            // Using BoxCollider
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                Debug.LogError("No BoxCollider found on this GameObject!");
                return Vector2.zero;
            }

            // Calculate the local hit point and normalize to UV
            Vector3 localHitPoint = transform.InverseTransformPoint(hit.point);
            uv = new Vector2(
                (localHitPoint.x / boxCollider.size.x) + 0.5f,       // Normalize X position
                1.0f - ((localHitPoint.y / boxCollider.size.y) + 0.5f) // Normalize and flip Y position
            );
        }
        else
        {
            // Using MeshCollider
            uv = hit.textureCoord;
            uv.y = 1.0f - uv.y; // Flip Y-axis
        }

        return uv;
    }

    private bool ShouldSkipInterpolation(Vector2 currentPosition, Vector2 lastPosition)
    {
        // Check if the texture space coordinates wrap around the edges
        float deltaX = Mathf.Abs(currentPosition.x - lastPosition.x);
        float deltaY = Mathf.Abs(currentPosition.y - lastPosition.y);

        bool crossesHorizontalEdge = deltaX > renderTexture.width / 16; // Crosses left-right edge
        bool crossesVerticalEdge = deltaY > renderTexture.height / 16; // Crosses top-bottom edge

        return crossesHorizontalEdge || crossesVerticalEdge;
    }

    private void InterpolateAndDraw(Vector2 lastPosition, Vector2 currentPosition, BrushSettings brush)
    {
        float distance = Vector2.Distance(currentPosition, lastPosition);
        int steps = Mathf.CeilToInt(distance / minBrushDistance);

        for (int i = 1; i <= steps; i++)
        {
            Vector2 interpolatedPosition = Vector2.Lerp(lastPosition, currentPosition, i / (float)steps);
            DrawAtPosition(interpolatedPosition, brush.color, brush.sizeX, brush.sizeY, brush.brushTransform.rotation.eulerAngles.z);
        }
    }

    private void DrawAtPosition(Vector2 position, Color color, float sizeX, float sizeY, float rotationAngle)
    {
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, renderTexture.width, renderTexture.height, 0);

        brushMaterial.SetPass(0);

        GL.Begin(GL.QUADS);
        GL.Color(color);

        // Convert rotation angle to radians
        float radians = rotationAngle * Mathf.Deg2Rad;

        // Calculate the rotation matrix components 
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        // Define the local offset vertices of the rectangle relative to the center
        Vector2[] vertices = new Vector2[4];
        vertices[0] = new Vector2(-sizeX, -sizeY); // Bottom-left
        vertices[1] = new Vector2(sizeX, -sizeY);  // Bottom-right
        vertices[2] = new Vector2(sizeX, sizeY);   // Top-right
        vertices[3] = new Vector2(-sizeX, sizeY);  // Top-left

        // Rotate each vertex to match the brush rotation
        for (int i = 0; i < vertices.Length; i++)
        {
            float rotatedX = vertices[i].x * cos + vertices[i].y * sin;
            float rotatedY = -vertices[i].x * sin + vertices[i].y * cos;

            // Add the position offset to align with the center
            GL.Vertex3(position.x + rotatedX, position.y + rotatedY, 0);
        }

        GL.End();
        GL.PopMatrix();
    }

    // Public utility methods
    public void ClearBoard()
    {
        if (renderTexture != null)
        {
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, backgroundColor);
            RenderTexture.active = null;
        }
    }

    public void SetBackgroundColor(Color newColor)
    {
        backgroundColor = newColor;

        // Update eraser brushes to match new background color
        foreach (var brush in brushes)
        {
            if (brush.isEraser)
            {
                brush.color = backgroundColor;
                brush.color.a = 1f;
            }
        }
    }

    // Add a brush at runtime
    public void AddBrush(BrushSettings newBrush)
    {
        if (newBrush.brushGrabbable != null)
        {
            newBrush.brushGrabbable.selectEntered.AddListener((args) => OnBrushGrabbed(newBrush, args));
            newBrush.brushGrabbable.selectExited.AddListener((args) => OnBrushReleased(newBrush, args));
        }

        newBrush.color.a = markerAlpha;
        if (newBrush.isEraser)
        {
            newBrush.color = backgroundColor;
            newBrush.color.a = 1f;
        }

        brushes.Add(newBrush);
    }

    // Remove a brush at runtime
    public void RemoveBrush(BrushSettings brushToRemove)
    {
        if (brushToRemove.brushGrabbable != null)
        {
            brushToRemove.brushGrabbable.selectEntered.RemoveAllListeners();
            brushToRemove.brushGrabbable.selectExited.RemoveAllListeners();
        }

        brushes.Remove(brushToRemove);
    }
}