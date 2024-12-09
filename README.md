# MPFPathfinder
Dynamic Player for MPF files produced by [MPFmaster]([https://pages.github.com/](https://github.com/xan1242/MPFmaster)). This is not a feature complete implementation, but it's sufficient for playing back NFS Most Wanted and NFS Carbon files.

## Usage

Example usage in Unity:

```CS
    void Start()
    {
        string fileName = "cb_mus_" + index;
        string[] data = File.ReadAllText(Path.GetFullPath(Application.dataPath + "/../Data/" + fileName + ".txt")).Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).ToArray();
        MusicPathfinder.Initialize(data);
        StartCoroutine(PlayPursuit());
    }

    ...

    IEnumerator PlayPursuit()
    {
        source.Stop();
        source2.Stop();
        source.clip = null;
        source2.clip = null;
        yield return 0;

        MusicPathfinder.PlayTrack(this, 0);

        int curClip = -1;

        double timeWhenClipEnds = AudioSettings.dspTime;
        double timeWhenPrevClipEnds = AudioSettings.dspTime;
        double nextNodeTime = timeWhenClipEnds;
        bool sourceSwitch = false;
        AudioClip currentClip = null;

        while (true)
        {
            if (AudioSettings.dspTime >= nextNodeTime || forceEnd)
            {
                if (forceEnd)
                {
                    forceEnd = false;
                    source.Stop();
                    source2.Stop();
                    timeWhenClipEnds = AudioSettings.dspTime;
                    timeWhenPrevClipEnds = AudioSettings.dspTime;
                    nextNodeTime = timeWhenClipEnds;
                }

                MusicPathfinder.OnNodeEnd(this);

                if (MusicPathfinder.GetDesiredClip() == 0 && MusicPathfinder.CurrentNode > 0)
                {
                    MusicPathfinder.OnNodeEnd(this);
                }

                var nextClip = MusicPathfinder.GetDesiredClip();

                if (nextClip == -1)
                {
                    nextClip = curClip + 1;
                }

                curClip = nextClip;

                sourceSwitch = !sourceSwitch;

                var curSource = sourceSwitch ? source : source2;
                var nextSource = sourceSwitch ? source2 : source;

                currentClip = null;

                if (nextClip >= 0)
                {
                    string filePath = Path.GetFullPath(Application.dataPath + "/../Data/MusicBank/cb_mus_" + currentBankIdx + "/" + nextClip + ".ogg").Replace("\\","/");
                    if (File.Exists(filePath))
                    {
                        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + filePath, AudioType.OGGVORBIS))
                        {
                            yield return www.SendWebRequest();

                            if (www.result == UnityWebRequest.Result.ConnectionError)
                            {
                                Debug.LogError(www.error);
                            }
                            else
                            {
                                currentClip = DownloadHandlerAudioClip.GetContent(www);
                            }
                        }
                    }
                }

                if (curSource.clip != null && nextSource.clip != curSource.clip)
                {
                    Destroy(curSource.clip);
                }

                curSource.clip = currentClip;

                timeWhenPrevClipEnds = timeWhenClipEnds;
                float add = currentClip != null ? currentClip.length : 1f;
                timeWhenClipEnds += add;

                curSource.PlayScheduled(timeWhenPrevClipEnds);

                nextNodeTime = timeWhenClipEnds - add * 0.5f;
            }

            volumeMod = MusicPathfinder.Volume / 127f;

            yield return 0;
        }
    }
```
