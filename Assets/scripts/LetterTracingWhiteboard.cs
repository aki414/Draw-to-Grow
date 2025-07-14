using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class LetterTracingWhiteboard : MonoBehaviour
{
    [Tooltip("The Render Texture to draw on")]
    public RenderTexture renderTexture;

    [Header("Letter Guide (Sprite Atlas)")]
    [Tooltip("Array of letter sprites (A-Z), sliced from a sprite sheet")] 
    public Sprite[] letterSprites; // Should be ordered A-Z
    [Tooltip("The index of the letter to trace (0 = first sprite)")]
    public int selectedLetterIndex = 0;
    [Tooltip("Alpha threshold for where drawing is allowed (0 = everywhere, 1 = only fully opaque)")]
    [Range(0, 1)]
    public float guideAlphaThreshold = 0.1f;

    [Header("Brush Settings")]
    public float maxDistance = 0.2f;
    public float minBrushDistance = 2f;
    public Color backgroundColor = Color.white;
    [Range(0, 1)]
    public float markerAlpha = 0.7f;
    public bool useBoxCollider = true;

    [System.Serializable]
    public class BrushSettings
    {
        public XRGrabInteractable brushGrabbable;
        public Transform brushTransform;
        public Color color = Color.black;
        public int sizeY = 20;
        public int sizeX = 20;
        public bool isEraser = false;
        [HideInInspector] public Vector2 lastPosition;
        [HideInInspector] public bool isFirstDraw = true;
        [HideInInspector] public bool isDrawing = false;
        [HideInInspector] public bool isGrabbed = false;
    }

    [Header("Add Brushes")]
    public List<BrushSettings> brushes = new List<BrushSettings>();

    private Material brushMaterial;
    private Sprite currentLetterSprite;
    private Texture2D currentLetterTexture;

    private void Start()
    {
        InitializeBrushMaterial();
        SetupRenderTexture();
        InitializeBrushes();
        UpdateCurrentLetterSprite();
        DrawLetterGuide();
    }

    private void OnValidate()
    {
        UpdateCurrentLetterSprite();
    }

    private void InitializeBrushMaterial()
    {
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
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.mainTexture = renderTexture;
        }
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
                brush.brushGrabbable.selectEntered.AddListener((args) => OnBrushGrabbed(brush, args));
                brush.brushGrabbable.selectExited.AddListener((args) => OnBrushReleased(brush, args));
            }
            brush.color.a = markerAlpha;
            if (brush.isEraser)
            {
                brush.color = backgroundColor;
                brush.color.a = 1f;
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
        foreach (BrushSettings brush in brushes)
        {
            if (brush.brushGrabbable != null)
            {
                brush.brushGrabbable.selectEntered.RemoveAllListeners();
                brush.brushGrabbable.selectExited.RemoveAllListeners();
            }
        }
    }

    public void Update()
    {
        if (renderTexture == null || brushMaterial == null) return;
        RenderTexture.active = renderTexture;
        foreach (var brush in brushes)
        {
            if (brush.brushGrabbable != null && brush.isGrabbed)
            {
                DrawBrushOnTexture(brush);
            }
        }
        RenderTexture.active = null;
    }

    private void DrawBrushOnTexture(BrushSettings brush)
    {
        if (brush.brushTransform == null) return;
        Ray ray = new Ray(brush.brushTransform.position, brush.brushTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
        {
            if (hit.collider.gameObject == gameObject)
            {
                Vector2 uv = CalculateUVCoordinates(hit);
                int x = (int)(uv.x * renderTexture.width);
                int y = (int)(uv.y * renderTexture.height);
                Vector2 currentPosition = new Vector2(x, y);
                if (!brush.isDrawing)
                {
                    brush.isFirstDraw = true;
                    brush.isDrawing = true;
                }
                if (brush.isFirstDraw)
                {
                    TryDrawAtPosition(currentPosition, brush);
                    brush.lastPosition = currentPosition;
                    brush.isFirstDraw = false;
                    return;
                }
                if (ShouldSkipInterpolation(currentPosition, brush.lastPosition))
                {
                    TryDrawAtPosition(currentPosition, brush);
                }
                else
                {
                    InterpolateAndDraw(brush.lastPosition, currentPosition, brush);
                }
                brush.lastPosition = currentPosition;
            }
        }
        else
        {
            brush.isDrawing = false;
        }
    }

    private Vector2 CalculateUVCoordinates(RaycastHit hit)
    {
        Vector2 uv;
        if (useBoxCollider)
        {
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                Debug.LogError("No BoxCollider found on this GameObject!");
                return Vector2.zero;
            }
            Vector3 localHitPoint = transform.InverseTransformPoint(hit.point);
            uv = new Vector2(
                (localHitPoint.x / boxCollider.size.x) + 0.5f,
                1.0f - ((localHitPoint.y / boxCollider.size.y) + 0.5f)
            );
        }
        else
        {
            uv = hit.textureCoord;
            uv.y = 1.0f - uv.y;
        }
        return uv;
    }

    private bool ShouldSkipInterpolation(Vector2 currentPosition, Vector2 lastPosition)
    {
        float deltaX = Mathf.Abs(currentPosition.x - lastPosition.x);
        float deltaY = Mathf.Abs(currentPosition.y - lastPosition.y);
        bool crossesHorizontalEdge = deltaX > renderTexture.width / 16;
        bool crossesVerticalEdge = deltaY > renderTexture.height / 16;
        return crossesHorizontalEdge || crossesVerticalEdge;
    }

    private void InterpolateAndDraw(Vector2 lastPosition, Vector2 currentPosition, BrushSettings brush)
    {
        float distance = Vector2.Distance(currentPosition, lastPosition);
        int steps = Mathf.CeilToInt(distance / minBrushDistance);
        for (int i = 1; i <= steps; i++)
        {
            Vector2 interpolatedPosition = Vector2.Lerp(lastPosition, currentPosition, i / (float)steps);
            TryDrawAtPosition(interpolatedPosition, brush);
        }
    }

    // Only draw if the guide sprite allows it
    private void TryDrawAtPosition(Vector2 position, BrushSettings brush)
    {
        if (IsWithinLetterGuide(position) || brush.isEraser)
        {
            DrawAtPosition(position, brush.color, brush.sizeX, brush.sizeY, brush.brushTransform != null ? brush.brushTransform.rotation.eulerAngles.z : 0f);
        }
    }

    // Check if the position is within the letter guide (alpha above threshold)
    private bool IsWithinLetterGuide(Vector2 position)
    {
        if (currentLetterTexture == null) return true; // If no guide, allow drawing everywhere

        // Calculate the area where the letter is drawn (centered, 50% size)
        float targetWidth = renderTexture.width * 0.5f;
        float targetHeight = renderTexture.height * 0.5f;
        float xOffset = (renderTexture.width - targetWidth) * 0.5f;
        float yOffset = (renderTexture.height - targetHeight) * 0.5f;

        // Check if the position is within the letter area
        if (position.x < xOffset || position.x >= xOffset + targetWidth ||
            position.y < yOffset || position.y >= yOffset + targetHeight)
        {
            return false; // Outside the letter area
        }

        // Map position to sprite rect
        Rect spriteRect = currentLetterSprite.rect;
        float normX = (position.x - xOffset) / targetWidth;
        float normY = (position.y - yOffset) / targetHeight;
        int spriteX = Mathf.Clamp((int)(normX * spriteRect.width), 0, (int)spriteRect.width - 1);
        int spriteY = Mathf.Clamp((int)(normY * spriteRect.height), 0, (int)spriteRect.height - 1);
        int texX = (int)spriteRect.x + spriteX;
        int texY = (int)spriteRect.y + spriteY;
        Color pixel = currentLetterTexture.GetPixel(texX, texY);
        return pixel.a > guideAlphaThreshold;
    }

    private void DrawAtPosition(Vector2 position, Color color, float sizeX, float sizeY, float rotationAngle)
    {
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, renderTexture.width, renderTexture.height, 0);
        brushMaterial.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.Color(color);
        float radians = rotationAngle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        Vector2[] vertices = new Vector2[4];
        vertices[0] = new Vector2(-sizeX, -sizeY);
        vertices[1] = new Vector2(sizeX, -sizeY);
        vertices[2] = new Vector2(sizeX, sizeY);
        vertices[3] = new Vector2(-sizeX, sizeY);
        for (int i = 0; i < vertices.Length; i++)
        {
            float rotatedX = vertices[i].x * cos + vertices[i].y * sin;
            float rotatedY = -vertices[i].x * sin + vertices[i].y * cos;
            GL.Vertex3(position.x + rotatedX, position.y + rotatedY, 0);
        }
        GL.End();
        GL.PopMatrix();
    }

    // Draw the selected letter sprite as the background
    private void DrawLetterGuide()
    {
        if (renderTexture == null || currentLetterSprite == null) return;
        RenderTexture.active = renderTexture;
        GL.Clear(true, true, backgroundColor);

        float targetWidth = renderTexture.width * 0.5f;
        float targetHeight = renderTexture.height * 0.5f;
        float x = (renderTexture.width - targetWidth) * 0.5f;
        float y = (renderTexture.height - targetHeight) * 0.5f;

        Graphics.DrawTexture(
            new Rect(x, y, targetWidth, targetHeight),
            currentLetterSprite.texture,
            currentLetterSprite.rect,
            0, 0, 0, 0
        );
        RenderTexture.active = null;
    }

    public void ClearBoard()
    {
        DrawLetterGuide();
    }

    public void SetBackgroundColor(Color newColor)
    {
        backgroundColor = newColor;
        foreach (var brush in brushes)
        {
            if (brush.isEraser)
            {
                brush.color = backgroundColor;
                brush.color.a = 1f;
            }
        }
        DrawLetterGuide();
    }

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

    public void RemoveBrush(BrushSettings brushToRemove)
    {
        if (brushToRemove.brushGrabbable != null)
        {
            brushToRemove.brushGrabbable.selectEntered.RemoveAllListeners();
            brushToRemove.brushGrabbable.selectExited.RemoveAllListeners();
        }
        brushes.Remove(brushToRemove);
    }

    // Update the current letter sprite and texture based on selectedLetterIndex
    private void UpdateCurrentLetterSprite()
    {
        if (letterSprites == null || letterSprites.Length == 0 || selectedLetterIndex < 0 || selectedLetterIndex >= letterSprites.Length)
        {
            currentLetterSprite = null;
            currentLetterTexture = null;
            return;
        }
        currentLetterSprite = letterSprites[selectedLetterIndex];
        currentLetterTexture = currentLetterSprite.texture;
    }

    // Call this to change the letter at runtime
    public void SetLetterByIndex(int index)
    {
        selectedLetterIndex = index;
        UpdateCurrentLetterSprite();
        DrawLetterGuide();
    }
}