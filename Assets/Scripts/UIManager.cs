using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
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
