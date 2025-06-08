using System;
using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    // Requisite Scene object References
    public List<Button> buttons;
    public List<DragInput> dragFields;
    public List<GameObject> panels;
    public Slider noiseTypeSlider;
    public Slider xAngleSlider;
    public Slider yAngleSlider;

    
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
        ColorBlock block = buttons[1].colors;
        block.colorMultiplier = .8f;
        buttons[1].colors = block;
        panels[1].SetActive(false);

        block = buttons[2].colors;
        block.colorMultiplier = .8f;
        buttons[2].colors = block;
        panels[2].SetActive(false);

        buttons[0].onClick.AddListener(() =>
        {
            block = buttons[0].colors;
            block.colorMultiplier = 1.0f;
            buttons[0].colors = block;
            
            block = buttons[1].colors;
            block.colorMultiplier = .8f;
            buttons[1].colors = block;
            
            block = buttons[2].colors;
            block.colorMultiplier = .8f;
            buttons[2].colors = block;
            
            panels[0].SetActive(true);
            panels[1].SetActive(false);
            panels[2].SetActive(false);
        });
        
        buttons[1].onClick.AddListener(() =>
        {
            block = buttons[0].colors;
            block.colorMultiplier = .8f;
            buttons[0].colors = block;
            
            block = buttons[1].colors;
            block.colorMultiplier = 1.0f;
            buttons[1].colors = block;
            
            block = buttons[2].colors;
            block.colorMultiplier = .8f;
            buttons[2].colors = block;
            
            panels[0].SetActive(false);
            panels[1].SetActive(true);
            panels[2].SetActive(false);
        });
        
        buttons[2].onClick.AddListener(() =>
        {
            block = buttons[0].colors;
            block.colorMultiplier = .8f;
            buttons[0].colors = block;
            
            block = buttons[1].colors;
            block.colorMultiplier = .8f;
            buttons[1].colors = block;
            
            block = buttons[2].colors;
            block.colorMultiplier = 1.0f;
            buttons[2].colors = block;
            
            panels[0].SetActive(false);
            panels[1].SetActive(false);
            panels[2].SetActive(true);
        });
        
        noiseTypeSlider.onValueChanged.AddListener(_ => HeightCurve());
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
        if (slider.value >= 1000000)
        {
            n = (slider.value / 1000000).ToString("F2") + "M";
        }
        else if (slider.value >= 1000) {
            n = Mathf.RoundToInt (slider.value / 1000) + "k";
        }
        t.text = text + ": " + n;
    }
    
    // Same as above, just higher precision on the string
    public void SliderValueSmall (GameObject s) {
        // Get text component of slider that was just changed
        var slider = s.GetComponentInChildren<Slider> ();
        var t = s.GetComponentInChildren<TMP_Text> ();
        string text = t.text;
        
        // Replace number portion of text with the updated number
        text = text.Substring (0, text.IndexOf (':'));
        string n = string.Format ("{0:0.###}", slider.value).Replace (',', '.');
        
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

    private void HeightCurve() {
        // Get text component of slider that was just changed
        TMP_Text t = noiseTypeSlider.GetComponentInChildren<TMP_Text> ();
        string text = t.text;
        
        // Replace number portion of text with the updated number
        text = text.Substring (0, text.IndexOf (':'));
        string n;
        
        // Get the proper noise type
        switch (noiseTypeSlider.value)
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
    
    // Resets all values in meshGen, unfortunately I need to access sliders and whatnot, so It can't be a JSON read
    // While most of this logic is from meshGen, I really want that class to be more focused on logic, so this is here instead
    public void ResetAllValues()
    {
        meshGen.resolution = 512;
        meshGen.sliders[0].value = 512;
        meshGen.heightMultiplier = 20.0f;
        meshGen.sliders[3].value = 20.0f;
        meshGen.noiseType = NoiseType.Simplex;
        meshGen.sliders[1].value = 1;
        meshGen.noiseScale = 0.5f;
        meshGen.sliders[2].value = 5;
        meshGen.octaves = 7;
        meshGen.sliders[4].value = 7;
        meshGen.persistence = .5f;
        meshGen.sliders[5].value = .5f;
        meshGen.lacunarity = 2.0f;
        meshGen.sliders[6].value = 2.0f;
        meshGen.warpStrength = 0.2f;
        meshGen.sliders[7].value = 0.2f;
        meshGen.warpFrequency = 0.5f;
        meshGen.sliders[8].value = 0.5f;
        meshGen.seed = 1174;
        dragFields[0].text = "1174";
        meshGen.offset = float2.zero;
        dragFields[1].text = "0";
        dragFields[2].text = "0";
        meshGen.smoothingPasses = 2;
        meshGen.sliders[9].value = 2;
        meshGen.skipErosion = false;
        meshGen.erosionToggle.isOn = true;
        meshGen.numRainDrops = 200000;
        meshGen.sliders[10].value = 200000;
        meshGen.steps = 24;
        meshGen.sliders[24].value = 24;
        meshGen.radius = 3;
        meshGen.sliders[17].value = 3;
        meshGen.inertia = .05f;
        meshGen.sliders[11].value = .05f;
        meshGen.sedimentMax = 4;
        meshGen.sliders[12].value = 4;
        meshGen.depositionRate = .3f;
        meshGen.sliders[13].value = .3f;
        meshGen.evaporationRate = 0.075f;
        meshGen.sliders[14].value = 0.075f;
        meshGen.softness = .2f;
        meshGen.sliders[15].value = 0.8f;
        meshGen.gravity = 4;
        meshGen.sliders[16].value = 4.0f;
        meshGen.minSlope = .01f;
        meshGen.sliders[18].value = .01f;
        meshGen.waterToggle.isOn = true;
        meshGen.noiseMapToggle.isOn = false;
        noiseTypeSlider.value = 0;
        HeightCurve();
        meshGen.sliders[19].value = 1.0f;
        meshGen.sliders[20].value = .25f;
        meshGen.sliders[21].value = .75f;
        meshGen.sliders[22].value = .25f;
        meshGen.sliders[23].value = .4f;
        xAngleSlider.value = 30.0f;
        yAngleSlider.value = 245.0f;
        meshGen.angle = 0;

        meshGen.isMeshDirty = true;
    }
}
