using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public List<Button> buttons;
    public List<GameObject> panels;

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
}
