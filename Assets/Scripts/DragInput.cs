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

    protected override void Awake()
    {
        base.Awake();
        meshGen = FindFirstObjectByType<MeshGenerator>();
    }

    private void UpdateDelta()
    {
        if (delta == 0) return;
        float newValue;
        
        // Decimal should be smaller increments than integer
        if (contentType == ContentType.DecimalNumber)
        {
            newValue = float.Parse(text) + delta / 1000;
        }
        else
        {
            newValue = int.Parse(text) + delta;
        }

        text = newValue.ToString(CultureInfo.CurrentUICulture);
        delta = 0;

        // The proper update statement will be called by an event afterward to update the appropriate value
    }
    
    // overriding to keep track of how much the mouse moved rather than just highlighting
    public override void OnDrag(PointerEventData eventData)
    {
        delta = eventData.delta.x;
        
        // The following code lets the mouse wrap the screen while the player is dragging
        Vector3 mousePosition = Input.mousePosition;
        float finalPosition = mousePosition.x + Input.mousePositionDelta.x;
        if (finalPosition <= 0)
        {
            Mouse.current.WarpCursorPosition(new Vector2(Screen.width + finalPosition, mousePosition.y));
            delta -= Screen.width;
        }
        else if (finalPosition > Screen.width)
        {
            Mouse.current.WarpCursorPosition(new Vector2(finalPosition - Screen.width, mousePosition.y));
            delta += Screen.width;
        }
        
        // This will trigger an event call to update targeted values
        UpdateDelta();
    }

    public void updateSeed(string s)
    {
        if (!int.TryParse(s, out int result) || result == meshGen.seed) return;
        meshGen.seed = result;
        meshGen.GenerateMap();
    }

    public void updateOffsetX(string s)
    {
        if (!float.TryParse(s, out float result) || Mathf.Approximately(result, meshGen.offset.x)) return;
        meshGen.offset.x = result;
        meshGen.GenerateMap();
    }

    public void updateOffsetY(string s)
    {
        if (!float.TryParse(s, out float result) || Mathf.Approximately(result, meshGen.offset.y)) return;
        meshGen.offset.y = result;
        meshGen.GenerateMap();
    }
}
