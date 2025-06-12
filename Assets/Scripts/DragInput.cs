using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;


// The entire point of this class is just so that the number on the text field updates if the user drags it
// As well as to change the cursor while the user is dragging
public class DragInput : TMP_InputField
{
    // Cursor state is shared for all instances
    private static int hoverCount = 0;
    private static readonly Vector2 hotspot = new(16, 16);
    
    private float delta;
    private MeshGenerator meshGen;
    private bool isWrapping;
    private Texture2D dragCursor;
    private bool isDragging = false;
    private bool isHovering = false;

    protected override void Awake()
    {
        base.Awake();
        meshGen = FindFirstObjectByType<MeshGenerator>();
        dragCursor = Resources.Load<Texture2D>("DragCursor");
    }
    
    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        hoverCount++;
        isHovering = true;
        
        // Need to display the drag cursor at this point
        UpdateCursor();
    }
    
    public override void OnPointerExit(PointerEventData eventData)
    {
        base.OnPointerExit(eventData);
        hoverCount--;
        isHovering = false;
        
        // Not dragging this field, so we might be able to change back now
        if (!isDragging) UpdateCursor();
    }
    
    public override void OnBeginDrag(PointerEventData eventData)
    {
        base.OnBeginDrag(eventData);
        isDragging = true;
        
        // We're now dragging, so the cursor will stay changed while leaving
        UpdateCursor();
    }
    
    public override void OnEndDrag(PointerEventData eventData)
    {
        base.OnEndDrag(eventData);
        isDragging = false;
        
        // We've stopped dragging, so the cursor will go back to normal as long as it's not over a different drag field
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        // If either altering the value or hovering over the field, the drag cursor should be displayed
        if (isDragging || isHovering)
        {
            Cursor.SetCursor(dragCursor, hotspot, CursorMode.ForceSoftware);
        }
        else if (hoverCount == 0)
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.ForceSoftware);
        }
    }

    private void UpdateDelta()
    {
        if (delta == 0) return;
        
        // Decimal should be smaller increments than integers
        if (contentType == ContentType.DecimalNumber)
        {
            text = (float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) + delta / 1000).ToString("F3", CultureInfo.InvariantCulture);
        }
        else
        {
            text = (int.Parse(text) + Mathf.CeilToInt(delta)).ToString(CultureInfo.InvariantCulture);
        }
        
        // The proper update statement will be called by an event afterward to update the appropriate value
        delta = 0;
    }
    
    // overriding to keep track of how much the mouse moved rather than just highlighting
    public override void OnDrag(PointerEventData eventData)
    {
        // Wrapping will unfortunately not work on WebGL or WebGPU due to security restrictions, but it's cool in the engine
        
        // Skip a frame if we just wrapped to prevent a large jump
        if (isWrapping)
        {
            isWrapping = false;
            return;
        }
        
        delta = eventData.delta.x;
        
        // The following code lets the mouse wrap the screen while the player is dragging
        Vector3 mousePosition = Input.mousePosition;
        float finalPosition = mousePosition.x + Input.mousePositionDelta.x;
        if (finalPosition <= 0)
        {
            Mouse.current.WarpCursorPosition(new Vector2(Screen.width + finalPosition, mousePosition.y));
            isWrapping = true;
        }
        else if (finalPosition > Screen.width)
        {
            Mouse.current.WarpCursorPosition(new Vector2(finalPosition - Screen.width, mousePosition.y));
            isWrapping = true;
        }
        
        // This will trigger an event call to update targeted values
        UpdateDelta();
    }
    
    // Any ui that are using the dragSliders are being updated here
    public void updateSeed(string s)
    {
        if (!int.TryParse(s, out int result) || result == meshGen.seed) return;
        meshGen.seed = result;
        meshGen.isMeshDirty = true;
    }

    public void updateOffsetX(string s)
    {
        if (!float.TryParse(s, out float result) || Mathf.Approximately(result, meshGen.offset.x)) return;
        meshGen.offset.x = result;
        meshGen.isMeshDirty = true;
    }

    public void updateOffsetY(string s)
    {
        if (!float.TryParse(s, out float result) || Mathf.Approximately(result, meshGen.offset.y)) return;
        meshGen.offset.y = result;
        meshGen.isMeshDirty = true;
    }
}
