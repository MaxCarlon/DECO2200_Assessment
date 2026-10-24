using UnityEngine;
using System.IO;

public class ScreenshotHandler : MonoBehaviour
{
    // The resolution multiplier (e.g., 2 means twice the size of the Game window)
    public int superSize = 1;

    void Update()
    {
        // Capture screenshot when pressing the 'J' key
        if (Input.GetKeyDown(KeyCode.J))
        {
            TakeScreenshot();
        }
    }

    public void TakeScreenshot()
    {
        // Generate a unique filename using a timestamp
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = $"Screenshot_{timestamp}.png";

        // Define the save path
        string fullPath = Path.Combine(Application.persistentDataPath, filename);

        // Capture the screenshot
        ScreenCapture.CaptureScreenshot(fullPath, superSize);

        Debug.Log($"Screenshot saved to: {fullPath}");
    }
}
