using UnityEngine;
using UnityEngine.Video;
using System.IO;

public class VideoPlayerObj : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;
    // Enter your exact file name including extension (e.g., "intro.mp4")
    public string videoFileName = "rat.mp4";

    void Start()
    {
        videoPlayer = GetComponent<VideoPlayer>();

        // Dynamically point to the WebGL streaming assets folder
        string videoPath = Path.Combine(Application.streamingAssetsPath, videoFileName);
        videoPlayer.url = videoPath;

        // Start playback (Consider calling this on a button click to prevent browser autoplay blocks)
        videoPlayer.Play();
    }
}
