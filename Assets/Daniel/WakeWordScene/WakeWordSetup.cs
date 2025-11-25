using LocalWake.Unity;
using UnityEngine;

public class WakeWordSetup : MonoBehaviour
{
    public WakeWordRecorder recorder;
    public WakeWordManager manager;

    // Call this after recording samples
    public void CommitReferences()
    {
        manager.SetReferences(recorder.WakeWordName, (System.Collections.Generic.IList<float[,]>)recorder.Samples);
    }

    public void ClearRecordedSamples()
    {
        recorder.ClearSamples();
    }
}
