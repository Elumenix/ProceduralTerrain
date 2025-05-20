using System;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public List<Button> buttons;
    public List<GameObject> panels;
    private bool dragging;
    public Light sceneLight;
    public MeshGenerator meshGen;
    public AnimationCurve NeutralCurve;
    public AnimationCurve MountainCurve;
    public AnimationCurve PlateauCurve;
    public AnimationCurve MesaCurve;
    public AnimationCurve BasinCurve;
    public AnimationCurve CanyonCurve;
    [Range(0,90.0f)]
    public float xAngle = 30.0f;
    [Range(0, 360.0f)]
    public float yAngle = 245.0f;

    public void Start()
    {
        panels[1].SetActive(false);
        panels[2].SetActive(false);

        buttons[0].onClick.AddListener(() =>
        {
            panels[0].SetActive(true);
            panels[1].SetActive(false);
            panels[2].SetActive(false);
        });
        
        buttons[1].onClick.AddListener(() =>
        {
            panels[0].SetActive(false);
            panels[1].SetActive(true);
            panels[2].SetActive(false);
        });
        
        buttons[2].onClick.AddListener(() =>
        {
            panels[0].SetActive(false);
            panels[1].SetActive(false);
            panels[2].SetActive(true);
        });
    }
    
    private void Update()
    {
        // Left mouse button just went down
        if (Input.GetMouseButtonDown(0))
        {
            // Toggle depending on whether the mouse is over ui this frame
            dragging = !EventSystem.current.IsPointerOverGameObject();
        }
        else if (dragging && !Input.GetMouseButton(0)) // No longer dragging
        {
            dragging = false;
        }

        if (dragging)
        {
            float movement = Input.mousePositionDelta.x;
            meshGen.angle -= movement * .25f;
        }
    }

    // Method of handling UI inspired by Sebastian Lague
    // Will be called directly by the sliders
    public void SliderValue (GameObject s) {
        // Get text component of slider that was just changed
        var slider = s.GetComponentInChildren<Slider> ();
        var t = s.GetComponentInChildren<TMP_Text> ();
        string text = t.text;
        
        // Replace number portion of text with the updated number
        text = text.Substring (0, text.IndexOf (':'));
        string n = string.Format ("{0:0.##}", slider.value).Replace (',', '.');
        
        // Add k when number gets very large to prevent extreme numbers
        if (slider.value >= 1000) {
            n = Mathf.RoundToInt (slider.value / 1000) + "k";
        }
        t.text = text + ": " + n;
    }
    
    public void SliderValueLiteral (GameObject s) {
        // Get text component of slider that was just changed
        var slider = s.GetComponentInChildren<Slider> ();
        var t = s.GetComponentInChildren<TMP_Text> ();
        string text = t.text;
        
        // Replace number portion of text with the updated number
        text = text.Substring (0, text.IndexOf (':'));
        string n = string.Format ("{0:0.##}", slider.value).Replace (',', '.');
        
        t.text = text + ": " + n;
    }
    
    public void NoiseValue (GameObject s) {
        // Get text component of slider that was just changed
        var slider = s.GetComponentInChildren<Slider> ();
        var t = s.GetComponentInChildren<TMP_Text> ();
        string text = t.text;
        
        // Replace number portion of text with the updated number
        text = text.Substring (0, text.IndexOf (':'));
        string n;
        
        // Get the proper noise type
        switch (slider.value)
        {
            default:
            case 0:
                n = "Perlin";
                break;
            
            case 1:
                n = "Simplex";
                break;
            
            case 2:
                n = "Worley";
                break;
        }
        
        t.text = text + ": " + n;
    }
    
    public void HeightCurve (GameObject s) {
        // Get text component of slider that was just changed
        var slider = s.GetComponentInChildren<Slider> ();
        var t = s.GetComponentInChildren<TMP_Text> ();
        string text = t.text;
        
        // Replace number portion of text with the updated number
        text = text.Substring (0, text.IndexOf (':'));
        string n;
        
        // Get the proper noise type
        switch (slider.value)
        {
            default:
            case 0:
                n = "Neutral";
                meshGen.heightCurve = NeutralCurve;
                break;
            
            case 1:
                n = "Mountains";
                meshGen.heightCurve = MountainCurve;
                break;
            
            case 2:
                n = "Plateaus";
                meshGen.heightCurve = PlateauCurve;
                break;
            case 3:
                n = "Mesa";
                meshGen.heightCurve = MesaCurve;
                break;
            case 4:
                n = "Basins";
                meshGen.heightCurve = BasinCurve;
                break;
            case 5:
                n = "Canyons";
                meshGen.heightCurve = CanyonCurve;
                break;
        }
        
        t.text = text + ": " + n;
        meshGen.isMeshDirty = true;
    }

    public void ChangeSunHeight(GameObject s)
    {
        var slider = s.GetComponentInChildren<Slider>();
        xAngle = slider.value;
        
        sceneLight.transform.eulerAngles = new Vector3(xAngle, yAngle, 0);
    }
    
    public void ChangeSunAngle(GameObject s)
    {
        var slider = s.GetComponentInChildren<Slider>();
        yAngle = slider.value;
        
        sceneLight.transform.eulerAngles = new Vector3(xAngle, yAngle, 0);
    }
}
