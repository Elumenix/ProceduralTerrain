using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;


// The entire point of this class is just so that the number on the text field updates if the user drags it
public class DragInput : TMP_InputField
{
    private float delta;
    private MeshGenerator meshGen;
    private bool isWrapping;

    protected override void Awake()
    {
        base.Awake();
        meshGen = FindFirstObjectByType<MeshGenerator>();
    }
    

    private void UpdateDelta()
    {
        if (delta == 0) return;
        
        // Decimal should be smaller increments than integer
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
