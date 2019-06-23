using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TheUltracube;
using UnityEngine;

using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of The Ultracube
/// Created by Timwi
/// </summary>
public class TheUltracubeModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMRuleSeedable RuleSeedable;
    public KMAudio Audio;
    public Transform Ultracube;
    public Transform[] Edges;
    public KMSelectable[] Vertices;
    public MeshFilter[] Faces;
    public Mesh Quad;
    public Material FaceMaterial;

    // Rule-seed
    private int[][] _colorPermutations;
    private List<bool?[]> _faces;

    private static int _moduleIdCounter = 1;
    private int _moduleId;

    private int[] _rotations;
    private float _hue, _sat, _v;
    private Coroutine _rotationCoroutine;
    private bool _transitioning;
    private int _progress;
    private int[] _vertexColors;
    private int _correctVertex;

    private Material _edgesMat, _verticesMat, _facesMat;
    private List<Mesh> _generatedMeshes = new List<Mesh>();
    private static readonly string[] _rotationNames = "XYZWV".SelectMany(one => "XYZWV".Where(two => two != one).Select(two => string.Concat(one, two))).ToArray();
    private static readonly string[][] _dimensionNames = new[] { new[] { "left", "right" }, new[] { "bottom", "top" }, new[] { "front", "back" }, new[] { "zig", "zag" }, new[] { "ping", "pong" } };
    private static readonly string[] _colorNames = new[] { "red", "yellow", "green", "blue" };
    private static readonly Color[] _vertexColorValues = "e54747,e5e347,47e547,3ba0f1".Split(',').Select(str => new Color(Convert.ToInt32(str.Substring(0, 2), 16) / 255f, Convert.ToInt32(str.Substring(2, 2), 16) / 255f, Convert.ToInt32(str.Substring(4, 2), 16) / 255f)).ToArray();
    private static readonly int[] _shapeOrder = { 4, 3, 1, 2, 0 };

    void Start()
    {
        this._moduleId = _moduleIdCounter++;

        this._edgesMat = this.Edges[0].GetComponent<MeshRenderer>().material;
        for (int i = 0; i < this.Edges.Length; i++)
            this.Edges[i].GetComponent<MeshRenderer>().sharedMaterial = this._edgesMat;

        this._verticesMat = this.Vertices[0].GetComponent<MeshRenderer>().material;
        for (int i = 0; i < this.Vertices.Length; i++)
            this.Vertices[i].GetComponent<MeshRenderer>().sharedMaterial = this._verticesMat;

        this._facesMat = this.Faces[0].GetComponent<MeshRenderer>().material;
        for (int i = 0; i < this.Faces.Length; i++)
            this.Faces[i].GetComponent<MeshRenderer>().sharedMaterial = this._facesMat;

        SetUltracube(getUnrotatedVertices().Select(p => p.Project()).ToArray(), setFaces: true);

        // RULE SEED
        var rnd = this.RuleSeedable.GetRNG();
        this._faces = new List<bool?[]>();

        for (var i = 0; i < _shapeOrder.Length; i++)
            for (int iv = 0; iv < 2; iv++)
                for (var j = i + 1; j < _shapeOrder.Length; j++)
                    for (int jv = 0; jv < 2; jv++)
                        for (var k = j + 1; k < _shapeOrder.Length; k++)
                            for (int kv = 0; kv < 2; kv++)
                                this._faces.Add(Enumerable.Range(0, 5).Select(d => d == _shapeOrder[i] ? iv != 0 : d == _shapeOrder[j] ? jv != 0 : d == _shapeOrder[k] ? kv != 0 : (bool?)null).ToArray());
        rnd.ShuffleFisherYates(this._faces);
        this._colorPermutations = rnd.ShuffleFisherYates(
            new[] { "RYGB", "RYBG", "RGYB", "RGBY", "RBYG", "RBGY", "YRGB", "YRBG", "YGRB", "YGBR", "YBRG", "YBGR", "GRYB", "GRBY", "GYRB", "GYBR", "GBRY", "GBYR", "BRYG", "BRGY", "BYRG", "BYGR", "BGRY", "BGYR" }
                .Select(str => str.Select(ch => "RYGB".IndexOf(ch)).ToArray()).ToArray()
        ).Take(20).ToArray();
        Debug.Log("F = " + this._faces.Count);

        // GENERATE PUZZLE
        this._rotations = new int[5];
        for (int i = 0; i < this._rotations.Length; i++)
        {
            var axes = "XYZWV".ToArray().Shuffle();
            this._rotations[i] = Array.IndexOf(_rotationNames, string.Concat(axes[0], axes[1]));
        }
        Debug.LogFormat(@"[The Ultracube #{0}] Rotations are: {1}", this._moduleId, this._rotations.Select(rot => _rotationNames[rot]).Join(", "));


        /* manual generation code
        var seq = "XY,XZ,XW,XV,YZ,YW,YV,ZW,ZV,WV".Split(',').ToList();
        Debug.Log("All rotations are: "+seq.Join(" "));
        string row = "<tr><th>{0}</th><td class=\"face\">{1}</td><td class=\"order\">{2}</td><th>{3}</th><td class=\"face\">{4}</td><td class=\"order\">{5}</td></tr>\n";
        string table = ""; // table html result


        for (int i = 0; i < seq.Count; i++)
        {
            table += string.Format(row,
                 seq[i],
                StringifyShape(_faces[Array.IndexOf(_rotationNames, seq[i])]).Replace(" face",null),
                _colorPermutations[Array.IndexOf(_rotationNames, seq[i])].Select(x => _colorNames[x].ToUpperInvariant().First()).Join(""),
                 seq[i].Reverse().Join(""),
                StringifyShape(_faces[Array.IndexOf(_rotationNames, seq[i][1].ToString() + seq[i][0])]).Replace(" face", null),
                _colorPermutations[Array.IndexOf(_rotationNames, seq[i][1].ToString() + seq[i][0])].Select(x => _colorNames[x].ToUpperInvariant().First()).Join("")); ;
        }         
         */

        for (var i = 0; i < 1 << 5; i++)
            this.Vertices[i].OnInteract = VertexClick(i);

        this._rotationCoroutine = StartCoroutine(RotateUltracube());
    }

    private Point5D[] getUnrotatedVertices()
    {
        return Enumerable.Range(0, 1 << 5).Select(i => new Point5D((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1, (i & 8) != 0 ? 1 : -1, (i & 16) != 0 ? 1 : -1)).ToArray();
    }

    private KMSelectable.OnInteractHandler VertexClick(int v)
    {
        return delegate
        {
            this.Vertices[v].AddInteractionPunch(.2f);
            if (this._transitioning)
                return false;

            if (this._rotationCoroutine != null)
            {
                this._progress = 0;
                StartCoroutine(ColorChange(setVertexColors: true));
            }
            else if (v == this._correctVertex)
            {
                this._progress++;
                if (this._progress == 4)
                {
                    Debug.LogFormat(@"[The Ultracube #{0}] Module solved.", this._moduleId);
                    this.Module.HandlePass();
                    StartCoroutine(ColorChange(keepGrey: true));
                    this.Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, this.transform);
                }
                else
                {
                    StartCoroutine(ColorChange(setVertexColors: true));
                }
            }
            else
            {
                Debug.LogFormat(@"[The Ultracube #{0}] Incorrect vertex {1} pressed; resuming rotations.", this._moduleId, StringifyShape(v));
                this.Module.HandleStrike();
                this._rotationCoroutine = StartCoroutine(RotateUltracube(delay: true));
            }
            return false;
        };
    }

    private string StringifyShape(bool?[] shape)
    {
        var strs = _shapeOrder.Select(d => shape[d] == null ? null : _dimensionNames[d][shape[d].Value ? 1 : 0]).Where(s => s != null).ToArray();
        return strs.Length == 0
            ? "ultracube"
            : strs.Join("-") + " " + (
                strs.Length == 1 ? "hypercube" :
                strs.Length == 2 ? "cube" :
                strs.Length == 3 ? "face" :
                strs.Length == 4 ? "edge" : "vertex");
    }
    private string StringifyShape(int vertex)
    {
        return StringifyShape(Enumerable.Range(0, 5).Select(d => (bool?)((vertex & (1 << d)) != 0)).ToArray());
    }

    private IEnumerator ColorChange(bool keepGrey = false, bool setVertexColors = false, bool delay = false)
    {
        this._transitioning = true;
        for (int i = 0; i < this.Vertices.Length; i++)
            this.Vertices[i].GetComponent<MeshRenderer>().sharedMaterial = this._verticesMat;

        var prevHue = .5f;
        var prevSat = 0f;
        var prevV = .5f;
        SetColor(prevHue, prevSat, prevV);

        if (keepGrey)
            yield break;

        yield return new WaitForSeconds(delay ? 2.22f : .22f);

        this._hue = Rnd.Range(0f, 1f);
        this._sat = Rnd.Range(.6f, .9f);
        this._v = Rnd.Range(.75f, 1f);

        var duration = 1.5f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            SetColor(Mathf.Lerp(prevHue, this._hue, elapsed / duration), Mathf.Lerp(prevSat, this._sat, elapsed / duration), Mathf.Lerp(prevV, this._v, elapsed / duration));
            yield return null;
            elapsed += Time.deltaTime;
        }
        SetColor(this._hue, this._sat, this._v);

        if (setVertexColors)
        {
            yield return new WaitUntil(() => this._rotationCoroutine == null);
            PlayRandomSound();

            var desiredFace = this._faces[this._rotations[this._progress]];
            var initialColors = Enumerable.Range(0, 4).ToList();
            var q = new Queue<int>();
            var colors = new int?[1 << 5];

            Debug.LogFormat(@"[The Ultracube #{0}] Stage {1} correct face: {2}", this._moduleId, this._progress + 1, StringifyShape(desiredFace));
            Debug.LogFormat(@"[The Ultracube #{0}] Stage {1} correct color: {2}", this._moduleId, this._progress + 1, _colorNames[this._colorPermutations[this._rotations[4]][this._progress]]);

            // Assign the four colors on the desired face
            for (int v = 0; v < 1 << 5; v++)
            {
                if (Enumerable.Range(0, 5).All(d => desiredFace[d] == null || ((v & (1 << d)) != 0) == desiredFace[d].Value))
                {
                    var ix = Rnd.Range(0, initialColors.Count);
                    colors[v] = initialColors[ix];
                    initialColors.RemoveAt(ix);
                    for (var d = 0; d < 5; d++)
                        q.Enqueue(v ^ (1 << d));

                    if (colors[v].Value == this._colorPermutations[this._rotations[4]][this._progress])
                    {
                        this._correctVertex = v;
                        Debug.LogFormat(@"[The Ultracube #{0}] Stage {1} correct vertex: {2}", this._moduleId, this._progress + 1, StringifyShape(this._correctVertex));
                    }
                }
            }

            // Assign the remaining colors as best as possible
            while (q.Count > 0)
            {
                var vx = q.Dequeue();
                if (colors[vx] != null)
                    continue;

                // For each color, determine how many faces would have a clash
                var numClashesPerColor = new int[4];
                for (var color = 0; color < 4; color++)
                    for (var d = 0; d < 5; d++)
                        for (var e = d + 1; e < 5; e++)
                            if (Enumerable.Range(0, 1 << 5).Any(v => (v & (1 << d)) == (vx & (1 << d)) && (v & (1 << e)) == (vx & (1 << e)) && colors[v] == color))
                                numClashesPerColor[color]++;

                var cs = Enumerable.Range(0, 4).ToArray();
                Array.Sort(numClashesPerColor, cs);
                colors[vx] = cs[0];

                cs = Enumerable.Range(0, 5).ToArray().Shuffle();
                for (var d = 0; d < 5; d++)
                    q.Enqueue(vx ^ (1 << cs[d]));
            }

            this._vertexColors = colors.Select(v => v.Value).ToArray();
            for (int v = 0; v < 1 << 5; v++)
                this.Vertices[v].GetComponent<MeshRenderer>().material.color = _vertexColorValues[this._vertexColors[v]];
        }

        this._transitioning = false;
    }

    private void PlayRandomSound()
    {
        this.Audio.PlaySoundAtTransform("Bleep" + Rnd.Range(1, 11), this.transform);
    }

    private void SetColor(float h, float s, float v)
    {
        this._edgesMat.color = Color.HSVToRGB(h, s, v);
        this._verticesMat.color = Color.HSVToRGB(h, s * .8f, v * .5f);
        var clr = Color.HSVToRGB(h, s * .8f, v * .75f);
        clr.a = .1f;
        this._facesMat.color = clr;
    }

    private IEnumerator RotateUltracube(bool delay = false)
    {
        var colorChange = ColorChange(delay: delay);
        while (colorChange.MoveNext())
            yield return colorChange.Current;

        var unrotatedVertices = Enumerable.Range(0, 1 << 5).Select(i => new Point5D((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1, (i & 8) != 0 ? 1 : -1, (i & 16) != 0 ? 1 : -1)).ToArray();
        SetUltracube(unrotatedVertices.Select(v => v.Project()).ToArray(), setFaces: true);

        while (!this._transitioning)
        {
            yield return new WaitForSeconds(Rnd.Range(1.75f, 2.25f));

            for (int rot = 0; rot < this._rotations.Length && !this._transitioning; rot++)
            {
                var axis1 = "XYZWV".IndexOf(_rotationNames[this._rotations[rot]][0]);
                var axis2 = "XYZWV".IndexOf(_rotationNames[this._rotations[rot]][1]);
                var duration = 2f;
                var elapsed = 0f;

                while (elapsed < duration)
                {
                    var angle = easeInOutQuad(elapsed, 0, Mathf.PI / 2, duration);
                    var matrix = new double[25];
                    for (int i = 0; i < 5; i++)
                        for (int j = 0; j < 5; j++)
                            matrix[i + 5 * j] =
                                i == axis1 && j == axis1 ? Mathf.Cos(angle) :
                                i == axis1 && j == axis2 ? Mathf.Sin(angle) :
                                i == axis2 && j == axis1 ? -Mathf.Sin(angle) :
                                i == axis2 && j == axis2 ? Mathf.Cos(angle) :
                                i == j ? 1 : 0;

                    SetUltracube(unrotatedVertices.Select(v => (v * matrix).Project()).ToArray(), setFaces: true);

                    yield return null;
                    elapsed += Time.deltaTime;
                }

                // Reset the position of the hypercube
                SetUltracube(unrotatedVertices.Select(v => v.Project()).ToArray(), setFaces: true);
                yield return new WaitForSeconds(Rnd.Range(.5f, .6f));
            }
        }

        this._transitioning = false;
        this._rotationCoroutine = null;
    }

    private static float easeInOutQuad(float t, float start, float end, float duration)
    {
        var change = end - start;
        t /= duration / 2;
        if (t < 1)
            return change / 2 * t * t + start;
        t--;
        return -change / 2 * (t * (t - 2) - 1) + start;
    }

    private void SetUltracube(Vector3[] vertices, bool setFaces = false)
    {
        // VERTICES
        for (int i = 0; i < 1 << 5; i++)
            this.Vertices[i].transform.localPosition = vertices[i];

        // EDGES
        var e = 0;
        for (int i = 0; i < 1 << 5; i++)
            for (int j = i + 1; j < 1 << 5; j++)
                if (((i ^ j) & ((i ^ j) - 1)) == 0)
                {
                    this.Edges[e].localPosition = (vertices[i] + vertices[j]) / 2;
                    this.Edges[e].localRotation = Quaternion.FromToRotation(Vector3.up, vertices[j] - vertices[i]);
                    this.Edges[e].localScale = new Vector3(.1f, (vertices[j] - vertices[i]).magnitude / 2, .1f);
                    e++;
                }

        // FACES
        if (setFaces)
        {
            foreach (var mesh in this._generatedMeshes)
                Destroy(mesh);
            this._generatedMeshes.Clear();

            var f = 0;
            for (int i = 0; i < 1 << 5; i++)
                for (int j = i + 1; j < 1 << 5; j++)
                {
                    var b1 = i ^ j;
                    var b2 = b1 & (b1 - 1);
                    if (b2 != 0 && (b2 & (b2 - 1)) == 0 && (i & b1 & ((i & b1) - 1)) == 0 && (j & b1 & ((j & b1) - 1)) == 0)
                    {
                        var mesh = new Mesh { vertices = new[] { vertices[i], vertices[i | j], vertices[i & j], vertices[j] }, triangles = new[] { 0, 1, 2, 1, 2, 3, 2, 1, 0, 3, 2, 1 } };
                        mesh.RecalculateNormals();
                        this._generatedMeshes.Add(mesh);
                        this.Faces[f].sharedMesh = mesh;
                        f++;
                    }
                }
        }
    }

    // copied from the original hypercube module.
#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} go [makes the ultracube stop rotating] | !{0} zig-bottom-front-left [presses a vertex when the hypercube is not rotating anymore]";
#pragma warning restore 414

    IEnumerator ProcessTwitchCommand(string command)
    {
        if (this._rotationCoroutine != null && Regex.IsMatch(command, @"^\s*(go|activate|stop|run|start|on|off)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            yield return new[] { this.Vertices[0] };
            yield break;
        }

        Match m;
        if (this._rotationCoroutine == null && (m = Regex.Match(command, string.Format(@"^\s*((?:{0})(?:[- ,;]*(?:{0}))*)\s*$", _dimensionNames.SelectMany(x => x).Join("|")), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            var elements = m.Groups[1].Value.Split(new[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (elements.Length != 5)
            {
                yield return "sendtochaterror Dude, it’s a 5D ultracube, you gotta have 5 dimensions.";
                yield break;
            }
            var dimensions = elements.Select(el => _dimensionNames.ToList().FindIndex(d => d.Any(dn => dn.EqualsIgnoreCase(el)))).ToArray();
            var invalid = Enumerable.Range(0, 4).SelectMany(i => Enumerable.Range(i + 1, 4 - i).Where(j => dimensions[i] == dimensions[j]).Select(j => new { i, j })).FirstOrDefault();
            if (invalid != null)
            {
                yield return elements[invalid.i].EqualsIgnoreCase(elements[invalid.j])
                    ? string.Format("sendtochaterror Dude, you wrote “{0}” twice.", elements[invalid.i], elements[invalid.j])
                    : string.Format("sendtochaterror Dude, “{0}” and “{1}” doesn’t jive.", elements[invalid.i], elements[invalid.j]);
                yield break;
            }
            var vertexIx = 0;
            for (int i = 0; i < 5; i++)
                vertexIx |= _dimensionNames[dimensions[i]].ToList().FindIndex(dn => dn.EqualsIgnoreCase(elements[i])) << dimensions[i];
            yield return null;
            yield return new[] { this.Vertices[vertexIx] };
        }
    }

    // -- end copy --
}
