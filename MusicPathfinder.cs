using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.IO;

public static class MusicPathfinder
{
    public class VersionInfoData
    {
        public int Major;
        public int Minor;
        public int Release;
        public int Prerelease;
        public int GenerateID;
        public int ProjectID;
        public int NumSections;
    }

    public class Router
    {
        public List<(int, int)> Pairs = new List<(int, int)>();
    }

    public class Track
    {
        public int StartingSample;
        public int SubBanks;
        public uint MusChecksum;
        public int MaxARAM;
        public int MaxMRAM;
    }

    public class Node
    {
        public class Branch
        {
            public int Min;
            public int Max;
            public int DestinationNode;
        }

        public uint Offset;
        public int Wave;
        public int Track;
        public int Section;
        public int Part;
        public int Router;
        public int Controller;
        public int Beats;
        public int Bars;
        public int SynchEvery;
        public int SynchOffset;
        public int Notes;
        public int Synch;
        public int ChannelBranching;
        public int Repeat;
        public uint Event;
        public List<Branch> Branches = new List<Branch>();
    }

    public class Event
    {
        public uint Offset;
        public List<EventAction> Action;
    }

    public class VarSource
    {
        public enum VarSourceType
        {
            Integer,
            Variable,
            Special
        }

        public VarSourceType Type;
        public int IntegerValue;
        public string VariableName;
    }

    public class Condition
    {
        public VarSource LeftVar;
        public string Op;
        public VarSource RightVar;
    }

    public class EventAction
    {
        public bool IsDelay;
        public VarSource DelayTime;
        public Action Action;
    }

    public class CoroutineAction
    {
        public Action Start;
        public Func<bool> Condition;
        public Action Action;
    }

    public static VersionInfoData VersionInfo;
    public static Dictionary<string, int> Variables = new Dictionary<string, int>();
    public static Dictionary<int, Router> Routers = new Dictionary<int, Router>();
    public static Dictionary<int, Track> Tracks = new Dictionary<int, Track>();
    public static Dictionary<int, Node> Nodes = new Dictionary<int, Node>();
    public static Dictionary<uint, Event> Events = new Dictionary<uint, Event>();

    public static int CurrentTrack;
    public static int CurrentNode;
    public static int Volume;
    public static int Intensity;
    public static int NextNode = -1;
    private static string[] SpecialTypes =
    {
        "CURRENTNODE",
        "CURRENTPART",
        "CURRENTSECTION",
        "VOLUME",
        // not needed:
        "CONTROLLER",
        "SPECIALVALUE_BAD",
        "EVENTEXPIRY",
        "EVENTPRIORITY",
        "FXBUS",
        "FXDRYLEVEL",
        "FXSENDLEVEL",
        "MAINVOICE",
        "NEXTNODE",
        "NOBRANCHING",
        "NODEDURATION",
        "PAUSE",
        "PITCHMULT",
        "PLAYINGNODE",
        "PLAYSTATUS",
        "RANDOMSHORT",
        "TIMENOW",
        "TIMETONEXTBEAT",
        "TIMETONEXTBAR",
        "TIMETONEXTNODE",
        "TIMESTRETCH",
        "BARDURATION",
        "BEATDURATION",
        "CURRENTCHANNELSET",
        "PLAYINGCHANNELSET"
    };

    private static string[] FadeTypes =
    {
        "VOLUME",
        // not needed:
        "INVALID",
        "SFXSENDLEVEL",
        "DRYLEVEL",
        "PITCHMULT",
        "STRETCHMULT",
        "CHANNELVOL",
        "PANANGLE",
        "PANDISTANCE",
        "PANSIZE",
        "PANTWIST"
    };

    private static List<EventAction> actionQueueBlocking = new List<EventAction>();
    private static List<CoroutineAction> actionQueueNonblocking = new List<CoroutineAction>();

    public static void Initialize(string fileName)
    {
        VersionInfo = new VersionInfoData();
        Variables.Clear();
        Routers.Clear();
        Tracks.Clear();
        Nodes.Clear();
        Events.Clear();

        string[] data = File.ReadAllText(Path.GetFullPath(Application.dataPath + "/../Data/"+ fileName + ".txt")).Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).ToArray();

        LoadData(data);
    }

    public static void LoadData(string[] data)
    {
        int lineNum = 0;

        while (lineNum < data.Length)
        {
            switch (data[lineNum++])
            {
                case "# Version info":
                    ParseVersionInfo(data, ref lineNum);
                    break;
                case "# Named vars":
                    ParseNamedVars(data, ref lineNum);
                    break;
                case "# Routers":
                    ParseRouters(data, ref lineNum);
                    break;
                case "# Tracks":
                    ParseTracks(data, ref lineNum);
                    break;
                case "# Nodes":
                    ParseNodes(data, ref lineNum);
                    break;
                case "# Events":
                    ParseEvents(data, ref lineNum);
                    break;
            }
        }
    }

    public static void PlayTrack(MonoBehaviour caller, int trackIndex)
    {
        CurrentTrack = trackIndex;
        Volume = 127;

        ChangeNode(caller, 0);
    }

    public static void OnNodeEnd(MonoBehaviour caller)
    {
        if (NextNode != -1)
        {
            if (NextNode == -2)
            {
                ChangeNode(caller, -1);
            }
            else
            {
                ChangeNode(caller, NextNode);
            }
            NextNode = -1;
            return;
        }

        if (!Nodes.ContainsKey(CurrentNode))
        {
            return;
        }

        Node node = Nodes[CurrentNode];

        bool branched = BranchOff(caller);

        if (!branched)
        {
            if (GetDesiredClip() == -1)
            {
                NextNode = -2;
            }

            ChangeNode(caller, CurrentNode++);
        }

        RunEvent(caller, node.Event);
    }

    public static void TriggerRouter(MonoBehaviour caller)
    {
        if (!Nodes.ContainsKey(CurrentNode))
        {
            return;
        }

        Node node = Nodes[CurrentNode];

        if (!Routers.ContainsKey(node.Router))
        {
            return;
        }

        Debug.Log("triggering router...");

        var router = Routers[node.Router];

        for (int i = 0; i < router.Pairs.Count; i++)
        {
            if (CurrentNode < router.Pairs[i].Item1) // todo correct condition
            {
                ChangeNode(caller, router.Pairs[i].Item2);
                return;
            }
        }
    }

    public static bool BranchOff(MonoBehaviour caller)
    {
        if (!Nodes.ContainsKey(CurrentNode))
        {
            return false;
        }

        Node node = Nodes[CurrentNode];

        foreach (var branch in node.Branches)
        {
            if (Intensity >= branch.Min && Intensity <= branch.Max)
            {
                ChangeNode(caller, branch.DestinationNode);

                return true;
            }
        }

        return false;
    }

    public static void ChangeNode(MonoBehaviour caller, int node)
    {
        CurrentNode = node;
    }

    public static int GetDesiredClip()
    {
        if (!Nodes.ContainsKey(CurrentNode))
        {
            return -2;
        }

        return Nodes[CurrentNode].Wave;
    }

    public static void RunEvent(MonoBehaviour caller, uint eventId)
    {
        if (!Events.ContainsKey(eventId))
        {
            return;
        }

        caller.StartCoroutine(ExecAction(caller, Events[eventId].Action));
    }

    private static IEnumerator ExecAction(MonoBehaviour caller, List<EventAction> script)
    {
        for (int i = 0; i < script.Count; i++)
        {
            if (script[i].IsDelay)
            {
                yield return new WaitForSeconds(EvalVar(script[i].DelayTime) * 0.001f);
            }
            else
            {
                int oldNode = CurrentNode;

                script[i].Action?.Invoke();

                if (actionQueueNonblocking.Count > 0)
                {
                    var actions = new List<CoroutineAction>(actionQueueNonblocking);
                    actionQueueNonblocking.Clear();

                    foreach (var coroutine in actions)
                    {
                        caller.StartCoroutine(RunCoroutineAction(caller, coroutine));
                    }
                }

                if (actionQueueBlocking.Count > 0)
                {
                    var stack = new List<EventAction>(actionQueueBlocking);
                    actionQueueBlocking.Clear();

                    yield return caller.StartCoroutine(ExecAction(caller, stack));
                }
            }
        }
    }

    private static IEnumerator RunCoroutineAction(MonoBehaviour caller, CoroutineAction coroutine)
    {
        coroutine.Start.Invoke();

        while (coroutine.Condition.Invoke())
        {
            coroutine.Action.Invoke();
            yield return 0;
        }
    }

    private static int EvalVar(VarSource source)
    {
        switch (source.Type)
        {
            case VarSource.VarSourceType.Integer:
                return source.IntegerValue;
            case VarSource.VarSourceType.Special:
                return GetSpecialVariable(source.VariableName);
            case VarSource.VarSourceType.Variable:
                return GetVariable(source.VariableName);
        }
        return 0;
    }

    private static void SetVar(VarSource source, int value)
    {
        switch (source.Type)
        {
            case VarSource.VarSourceType.Integer:
                throw new Exception("can't set integer");
            case VarSource.VarSourceType.Special:
                SetSpecialVariable(source.VariableName, value);
                break;
            case VarSource.VarSourceType.Variable:
                SetVariable(source.VariableName, value);
                break;
        }
    }

    private static void ParseVersionInfo(string[] data, ref int lineNum)
    {
        lineNum++;

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "#------------------------------------------------------------------------")
            {
                return;
            }

            var tokens = line.Trim().Split(' ');

            switch (tokens[0])
            {
                case "Major":
                    VersionInfo.Major = int.Parse(tokens[1]);
                    break;
                case "Minor":
                    VersionInfo.Minor = int.Parse(tokens[1]);
                    break;
                case "Release":
                    VersionInfo.Release = int.Parse(tokens[1]);
                    break;
                case "Prerelease":
                    VersionInfo.Prerelease = int.Parse(tokens[1]);
                    break;
                case "GenerateID":
                    VersionInfo.GenerateID = int.Parse(tokens[1]);
                    break;
                case "ProjectID":
                    VersionInfo.ProjectID = int.Parse(tokens[1]);
                    break;
                case "NumSections":
                    VersionInfo.NumSections = int.Parse(tokens[1]);
                    break;
                default:
                    Debug.LogError("unexpected version field " + tokens[0]);
                    break;
            }
        }
    }

    private static void ParseNamedVars(string[] data, ref int lineNum)
    {
        Variables.Clear();

        lineNum++;

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "#------------------------------------------------------------------------")
            {
                return;
            }

            if (line == "Var")
            {
                lineNum++;
                var tokens = data[lineNum++].Trim().Split(' ');
                lineNum++;

                Variables.Add(tokens[0], int.Parse(tokens[2]));
            }
            else
            {
                Debug.LogError("unexpected var header " + line);
            }
        }
    }
    
    private static void ParseRouters(string[] data, ref int lineNum)
    {
        Routers.Clear();

        lineNum++;

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "#------------------------------------------------------------------------")
            {
                return;
            }

            var headerTokens = line.Trim().Split(' ');

            if (headerTokens[0] == "Router")
            {
                lineNum++;
                ParseRouter(data, ref lineNum, int.Parse(headerTokens[1]));
            }
            else
            {
                Debug.LogError("unexpected router header " + headerTokens[0]);
            }
        }
    }

    private static void ParseRouter(string[] data, ref int lineNum, int routerNum)
    {
        var router = new Router();

        Routers.Add(routerNum, router);

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "}")
            {
                return;
            }

            var tokens = line.Trim().Split(' ');

            router.Pairs.Add((int.Parse(tokens[0]), int.Parse(tokens[2])));
        }
    }

    private static void ParseTracks(string[] data, ref int lineNum)
    {
        Tracks.Clear();

        lineNum++;

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "#------------------------------------------------------------------------")
            {
                return;
            }

            var headerTokens = line.Trim().Split(' ');

            if (headerTokens[0] == "Track")
            {
                lineNum++;
                ParseTrack(data, ref lineNum, int.Parse(headerTokens[1]));
            }
            else
            {
                Debug.LogError("unexpected track header " + headerTokens[0]);
            }
        }
    }

    private static void ParseTrack(string[] data, ref int lineNum, int trackNum)
    {
        var track = new Track();

        Tracks.Add(trackNum, track);

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "}")
            {
                return;
            }

            var tokens = line.Trim().Split(' ');

            switch (tokens[0])
            {
                case "StartingSample":
                    track.StartingSample = int.Parse(tokens[1]);
                    break;
                case "SubBanks":
                    track.SubBanks = int.Parse(tokens[1]);
                    break;
                case "MusChecksum":
                    track.MusChecksum = Convert.ToUInt32(tokens[1], 16);
                    break;
                case "MaxARAM":
                    track.MaxARAM = int.Parse(tokens[1]);
                    break;
                case "MaxMRAM":
                    track.MaxMRAM = int.Parse(tokens[1]);
                    break;
                default:
                    Debug.LogError("unexpected track field " + tokens[0]);
                    break;
            }
        }
    }

    private static void ParseNodes(string[] data, ref int lineNum)
    {
        Nodes.Clear();

        lineNum++;

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "#------------------------------------------------------------------------")
            {
                return;
            }

            var headerTokens = line.Trim().Split(' ');

            if (headerTokens[0] == "Node")
            {
                lineNum++;
                ParseNode(data, ref lineNum, int.Parse(headerTokens[1]), Convert.ToUInt32(headerTokens[3], 16));
            }
            else
            {
                Debug.LogError("unexpected node header " + headerTokens[0]);
            }
        }
    }

    private static void ParseNode(string[] data, ref int lineNum, int nodeNum, uint nodeOffset)
    {
        var node = new Node();

        node.Offset = nodeOffset;

        Nodes.Add(nodeNum, node);

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "}")
            {
                return;
            }

            var tokens = line.Trim().Split(' ');

            switch (tokens[0])
            {
                case "Wave":
                    node.Wave = int.Parse(tokens[1]);
                    break;
                case "Track":
                    node.Track = int.Parse(tokens[1]);
                    break;
                case "Section":
                    node.Section = int.Parse(tokens[1]);
                    break;
                case "Part":
                    node.Part = int.Parse(tokens[1]);
                    break;
                case "Router":
                    node.Router = int.Parse(tokens[1]);
                    break;
                case "Controller":
                    node.Controller = int.Parse(tokens[1]);
                    break;
                case "Beats":
                    node.Beats = int.Parse(tokens[1]);
                    break;
                case "Bars":
                    node.Bars = int.Parse(tokens[1]);
                    break;
                case "SynchEvery":
                    node.SynchEvery = int.Parse(tokens[1]);
                    break;
                case "SynchOffset":
                    node.SynchOffset = int.Parse(tokens[1]);
                    break;
                case "Notes":
                    node.Notes = int.Parse(tokens[1]);
                    break;
                case "Synch":
                    node.Synch = int.Parse(tokens[1]);
                    break;
                case "ChannelBranching":
                    node.ChannelBranching = int.Parse(tokens[1]);
                    break;
                case "Repeat":
                    node.Repeat = int.Parse(tokens[1]);
                    break;
                case "Event":
                    node.Event = Convert.ToUInt32(tokens[1], 16);
                    break;
                case "Branches":
                    ParseNodeBranches(data, ref lineNum, node);
                    break;
                default:
                    Debug.LogError("unexpected node field " + tokens[0]);
                    break;
            }
        }
    }

    private static void ParseNodeBranches(string[] data, ref int lineNum, Node node)
    {
        lineNum++;

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line.Trim() == "}")
            {
                return;
            }

            var tokens = line.Trim().Split(' ');

            if (tokens[0] == "Control")
            {
                var branch = new Node.Branch();

                var minmax = tokens[1].Split(',');

                branch.Min = int.Parse(minmax[0]);
                branch.Max = int.Parse(minmax[1]);
                branch.DestinationNode = int.Parse(tokens[3]);

                node.Branches.Add(branch);
            }
            else
            {
                Debug.LogError("unexpected node branch kind " + tokens[0]);
            }
        }
    }

    private static void ParseEvents(string[] data, ref int lineNum)
    {
        Events.Clear();

        lineNum++;

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "#------------------------------------------------------------------------")
            {
                return;
            }

            var headerTokens = line.Trim().Split(' ');

            if (headerTokens[0] == "Event")
            {
                lineNum++;
                ParseEvent(data, ref lineNum, Convert.ToUInt32(headerTokens[1], 16), Convert.ToUInt32(headerTokens[3], 16));
            }
            else
            {
                Debug.LogError("unexpected event section " + headerTokens[0]);
            }
        }
    }

    private static void ParseEvent(string[] data, ref int lineNum, uint eventId, uint eventOffset)
    {
        var result = new Event();

        result.Offset = eventOffset;

        Events.Add(eventId, result);

        while (lineNum < data.Length)
        {
            var line = data[lineNum++];

            if (line == "}")
            {
                return;
            }

            var tokens = line.Trim().Split(' ');

            switch (tokens[0])
            {
                case "Actions":
                    result.Action = ParseEventAction(data, ref lineNum);
                    break;
                default:
                    Debug.LogError("unexpected event section " + tokens[0]);
                    break;
            }
        }
    }

    private static VarSource ParseVariable(string str)
    {
        var s = new VarSource();

        if (str.Contains("["))
        {
            s.Type = VarSource.VarSourceType.Variable;
            s.VariableName = str.Split('[')[1].Split(']')[0];
        }
        else if (SpecialTypes.Contains(str))
        {
            s.Type = VarSource.VarSourceType.Special;
            s.VariableName = str;
        }
        else
        {
            s.Type = VarSource.VarSourceType.Integer;
            s.IntegerValue = int.Parse(str);
        }

        return s;
    }

    private static int GetVariable(string name)
    {
        if (Variables.TryGetValue(name, out int value))
        {
            return value;
        }

        throw new Exception("variable " + name + " not found");
    }

    private static void SetVariable(string name, int value)
    {
        if (Variables.ContainsKey(name))
        {
            Variables[name] = value;
            return;
        }

        throw new Exception("variable " + name + " not found");
    }

    public static int GetSpecialVariable(string name)
    {
        switch (name)
        {
            case "VOLUME":
                return Volume;
            case "CURRENTNODE":
                return CurrentNode;
            case "CURRENTPART":
                return Nodes.ContainsKey(CurrentNode) ? Nodes[CurrentNode].Part : 0;
            case "CURRENTSECTION":
                return Nodes.ContainsKey(CurrentNode) ? Nodes[CurrentNode].Section : 0;
            case "CONTROLLER":
                return Intensity;
        }

        throw new Exception("unsupported special variable " + name);
    }

    public static void SetSpecialVariable(string name, int value)
    {
        switch (name)
        {
            case "VOLUME":
                Volume = value;
                return;
            case "CURRENTNODE":
            case "CURRENTPART":
            case "CURRENTSECTION":
            case "CONTROLLER":
                throw new Exception("can't set " + name);
        }

        throw new Exception("unsupported special variable " + name);
    }

    private static int ParseHex(string hex)
    {
        if (hex.StartsWith("0x"))
        {
            hex = hex.Substring(2);
        }
        return Convert.ToInt32(hex, 16);
    }

    private static List<EventAction> ParseEventAction(string[] data, ref int lineNum)
    {
        lineNum++;
        
        List<EventAction> result = new List<EventAction>();

        while (lineNum < data.Length)
        {
            var line = data[lineNum++].Trim();

            if (line == "}")
            {
                return result;
            }

            if (line == "Elif")
            {
                throw new Exception("unexpected elif");
            }

            if (line == "Endif")
            {
                throw new Exception("unexpected endif");
            }

            var tokens = line.Split(new string[] { " ", "\t" }, StringSplitOptions.RemoveEmptyEntries);

            int track = ParseHex(tokens[0].Replace("(", "").Replace(",", ""));
            int sectionId = ParseHex(tokens[1].Replace(")", ""));

            tokens = tokens.Skip(2).ToArray();

            EventAction resultingAction = new EventAction();

            switch (tokens[0])
            {
                case "Wait":
                    var ms = ParseVariable(tokens[1]);

                    resultingAction.IsDelay = true;
                    resultingAction.DelayTime = ms;
                    break;
                case "Branch":
                    var node = ParseVariable(tokens[1]);
                    int ofSection = int.Parse(tokens[2].Replace("(", "").Replace(",", ""));
                    int immediate = int.Parse(tokens[3].Replace(")", ""));

                    resultingAction.Action = () =>
                    {
                        Debug.Log("<color=#00FF00>" + line + "</color>");

                        var nodeId = EvalVar(node);

                        NextNode = nodeId;
                    };
                    break;
                case "Fade":
                    string id = tokens[1];
                    int toVol = int.Parse(tokens[2].Replace("(", "").Replace(",", ""));
                    int flip = int.Parse(tokens[3].Replace(",", ""));
                    var ms2 = ParseVariable(tokens[4].Replace(")", ""));

                    CoroutineAction fadeAction = new CoroutineAction();

                    int startValue = 0;
                    float startTime = 0f;
                    float endTime = 0f;

                    fadeAction.Start = () =>
                    {
                        startTime = Time.time;
                        endTime += Time.time;
                    };

                    fadeAction.Condition = () =>
                    {
                        return Time.time < endTime;
                    };

                    fadeAction.Action = () =>
                    {
                        SetSpecialVariable(id, (int)Mathf.Lerp(startValue, toVol, Mathf.InverseLerp(startTime, endTime, Time.time)));
                    };

                    resultingAction.Action = () =>
                    {
                        Debug.Log("<color=#00FF00>" + line + "</color>");

                        startValue = GetSpecialVariable(id);
                        endTime = EvalVar(ms2) * 0.001f;

                        actionQueueNonblocking.Add(fadeAction);
                    };
                    break;
                case "Set":
                    var setWhat = ParseVariable(tokens[1]);
                    var toWhat = ParseVariable(tokens[3]);

                    switch (setWhat.Type)
                    {
                        case VarSource.VarSourceType.Integer:
                            throw new Exception("unable to set because not a variable");
                        case VarSource.VarSourceType.Special:
                            switch (toWhat.Type)
                            {
                                case VarSource.VarSourceType.Integer:
                                    resultingAction.Action = () =>
                                    {
                                        Debug.Log("<color=#00FF00>" + line + "</color>");
                                        SetSpecialVariable(setWhat.VariableName, toWhat.IntegerValue);
                                    };
                                    break;
                                case VarSource.VarSourceType.Special:
                                    resultingAction.Action = () =>
                                    {
                                        Debug.Log("<color=#00FF00>" + line + "</color>");
                                        SetSpecialVariable(setWhat.VariableName, GetSpecialVariable(toWhat.VariableName));
                                    };
                                    break;
                                case VarSource.VarSourceType.Variable:
                                    resultingAction.Action = () =>
                                    {
                                        Debug.Log("<color=#00FF00>" + line + "</color>");
                                        SetSpecialVariable(setWhat.VariableName, GetVariable(toWhat.VariableName));
                                    };
                                    break;
                            }
                            break;
                        case VarSource.VarSourceType.Variable:
                            switch (toWhat.Type)
                            {
                                case VarSource.VarSourceType.Integer:
                                    resultingAction.Action = () =>
                                    {
                                        Debug.Log("<color=#00FF00>" + line + "</color>");
                                        SetVariable(setWhat.VariableName, toWhat.IntegerValue);
                                    };
                                    break;
                                case VarSource.VarSourceType.Special:
                                    resultingAction.Action = () =>
                                    {
                                        Debug.Log("<color=#00FF00>" + line + "</color>");
                                        SetVariable(setWhat.VariableName, GetSpecialVariable(toWhat.VariableName));
                                    };
                                    break;
                                case VarSource.VarSourceType.Variable:
                                    resultingAction.Action = () =>
                                    {
                                        Debug.Log("<color=#00FF00>" + line + "</color>");
                                        SetVariable(setWhat.VariableName, GetVariable(toWhat.VariableName));
                                    };
                                    break;
                            }
                            break;
                    }
                    break;
                case "Event":
                    uint eventId = Convert.ToUInt32(tokens[1], 16);

                    resultingAction.Action = () =>
                    {
                        Debug.Log("<color=#00FF00>" + line + "</color>");

                        actionQueueBlocking.AddRange(Events[eventId].Action);
                    };
                    break;
                case "Callback":
                    var value = ParseVariable(tokens[1]);
                    var id2 = ParseVariable(tokens[2].Replace("(", "").Replace(")", ""));

                    resultingAction.Action = () =>
                    {
                        Debug.Log("<color=#00FF00>" + line + "</color>");

                        int valueVal = EvalVar(value);
                        int idVal = EvalVar(id2);

                        switch (valueVal)
                        {
                            case 0:
                                Variables["pursuitid"] = idVal;
                                break;
                            case 2:
                                // stop music
                                break;
                            case 3:
                                // run event
                                break;
                        }
                    };
                    break;
                case "Calc":
                    var value2 = ParseVariable(tokens[1]);
                    var op = tokens[2];
                    var by = ParseVariable(tokens[3]);

                    switch (op)
                    {
                        case "+":
                            resultingAction.Action = () =>
                            {
                                Debug.Log("<color=#00FF00>" + line + "</color>");

                                SetVar(value2, EvalVar(value2) + EvalVar(by));
                            };
                            break;
                        case "-":
                            resultingAction.Action = () =>
                            {
                                Debug.Log("<color=#00FF00>" + line + "</color>");

                                SetVar(value2, EvalVar(value2) - EvalVar(by));
                            };
                            break;
                        case "*":
                            resultingAction.Action = () =>
                            {
                                Debug.Log("<color=#00FF00>" + line + "</color>");

                                SetVar(value2, EvalVar(value2) * EvalVar(by));
                            };
                            break;
                        case "/":
                            resultingAction.Action = () =>
                            {
                                Debug.Log("<color=#00FF00>" + line + "</color>");

                                SetVar(value2, EvalVar(value2) / EvalVar(by));
                            };
                            break;
                        case "%":
                            resultingAction.Action = () =>
                            {
                                Debug.Log("<color=#00FF00>" + line + "</color>");

                                SetVar(value2, EvalVar(value2) % EvalVar(by));
                            };
                            break;
                    }
                    break;
                case "If":

                    List<int> ifs = new List<int>();

                    ifs.Add(lineNum - 1);

                    int endif = -1;

                    List<Condition> conditions = new List<Condition>();

                    var ifCond = new Condition();
                    ifCond.LeftVar = ParseVariable(tokens[1]);
                    ifCond.Op = tokens[2];
                    ifCond.RightVar = ParseVariable(tokens[3]);
                    conditions.Add(ifCond);

                    int depth = 0;

                    while (lineNum < data.Length && endif < 0)
                    {
                        var line2 = data[lineNum++].Trim();

                        if (line2 == "}")
                        {
                            throw new Exception("unexpected end of if");
                        }

                        var tokens2 = line2.Split(new string[] { " ", "\t" }, StringSplitOptions.RemoveEmptyEntries);

                        int track2 = ParseHex(tokens2[0].Replace("(", "").Replace(",", ""));
                        int sectionId2 = ParseHex(tokens2[1].Replace(")", ""));

                        tokens2 = tokens2.Skip(2).ToArray();

                        switch (tokens2[0])
                        {
                            case "Elif":
                                if (depth == 0)
                                {
                                    ifs.Add(lineNum - 1);

                                    ifCond = new Condition();
                                    ifCond.LeftVar = ParseVariable(tokens2[1]);
                                    ifCond.Op = tokens2[2];
                                    ifCond.RightVar = ParseVariable(tokens2[3]);
                                    conditions.Add(ifCond);
                                }
                                break;

                            case "Else":
                                if (depth == 0)
                                {
                                    ifs.Add(lineNum - 1);

                                    ifCond = new Condition();
                                    ifCond.LeftVar = new VarSource();
                                    ifCond.LeftVar.Type = VarSource.VarSourceType.Integer;
                                    ifCond.LeftVar.IntegerValue = 1;
                                    ifCond.Op = "==";
                                    ifCond.RightVar = new VarSource();
                                    ifCond.RightVar.Type = VarSource.VarSourceType.Integer;
                                    ifCond.RightVar.IntegerValue = 1;
                                    conditions.Add(ifCond);
                                }
                                break;
                                
                            case "Endif":
                                if (depth == 0)
                                {
                                    endif = lineNum - 1;
                                }

                                depth--;

                                break;
                            
                            case "If":
                                depth++;
                                break;
                        }

                        if (depth < 0)
                        {
                            break;
                        }
                    }

                    lineNum = endif + 1;

                    List<List<EventAction>> branches = new List<List<EventAction>>();

                    for (int i = 0; i < conditions.Count; i++)
                    {
                        string[] branchLines = data.Skip(ifs[i]).Take((i < conditions.Count - 1 ? ifs[i + 1] : endif) - ifs[i]).ToArray();

                        int localLineNum = 0;

                        branches.Add(ParseEventAction(branchLines, ref localLineNum));
                    }

                    resultingAction.Action = () =>
                    {
                        for (int i = 0; i < conditions.Count; i++)
                        {
                            var cond = conditions[i];

                            bool result = false;

                            switch (cond.Op)
                            {
                                case "==":
                                    result = EvalVar(cond.LeftVar) == EvalVar(cond.RightVar);
                                    break;
                                case "!=":
                                    result = EvalVar(cond.LeftVar) != EvalVar(cond.RightVar);
                                    break;
                                case ">":
                                    result = EvalVar(cond.LeftVar) > EvalVar(cond.RightVar);
                                    break;
                                case "<":
                                    result = EvalVar(cond.LeftVar) < EvalVar(cond.RightVar);
                                    break;
                                case ">=":
                                    result = EvalVar(cond.LeftVar) >= EvalVar(cond.RightVar);
                                    break;
                                case "<=":
                                    result = EvalVar(cond.LeftVar) <= EvalVar(cond.RightVar);
                                    break;
                            }

                            if (result)
                            {
                                actionQueueBlocking.AddRange(branches[i]);
                                return;
                            }
                        }
                    };
                    break;
                // not needed
                case "Pause":
                case "LoadBank":
                case "WaitBeat":
                case "DryFade":
                case "SFXFade":
                case "PitchFade":
                case "StretchFade":
                case "Filter":
                case "Action":
                case "Elif":
                case "Else":
                case "Endif":
                    Debug.LogWarning("nop " + tokens[0]);
                    break;
                default:
                    Debug.LogError("unexpected command " + tokens[0]);
                    break;
            }

            result.Add(resultingAction);
        }

        return result;
    }
}
