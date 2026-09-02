using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RocketLauncherMod
{
	public static class ObjLoader
	{
	public static Mesh LoadMesh(string path, float scale = 1f, float rotateX = 0f)
	{
		if (!File.Exists(path))
		{
			Debug.LogWarning("[RocketLauncherMod] Missing mesh: " + path);
			return CreateFallbackMesh();
		}
		List<Vector3> list = new List<Vector3>(4096);
		List<Vector3> list2 = new List<Vector3>(4096);
		List<Vector2> list3 = new List<Vector2>(4096);
		List<Vector3> list4 = new List<Vector3>(8192);
		List<Vector3> list5 = new List<Vector3>(8192);
		List<Vector2> list6 = new List<Vector2>(8192);
		List<int> list7 = new List<int>(24576);
		Dictionary<string, int> dictionary = new Dictionary<string, int>(32768);
		int num = 0;
		Quaternion rotation = Quaternion.Euler(rotateX, 0f, 0f);
		string[] array = File.ReadAllLines(path, Encoding.UTF8);
		foreach (string input in array)
		{
			if (string.IsNullOrEmpty(input) || input[0] == '#')
			{
				continue;
			}
			if (input.StartsWith("v "))
			{
				string[] array2 = SplitParts(input);
				Vector3 vector = new Vector3(Parse(array2[1]), Parse(array2[2]), Parse(array2[3])) * scale;
				list.Add(rotation * vector);
			}
			else if (input.StartsWith("vt "))
			{
				string[] array3 = SplitParts(input);
				list3.Add(new Vector2(Parse(array3[1]), Parse(array3[2])));
			}
			else if (input.StartsWith("vn "))
			{
				string[] array4 = SplitParts(input);
				list5.Add(new Vector3(Parse(array4[1]), Parse(array4[2]), Parse(array4[3])));
			}
			else if (input.StartsWith("f "))
			{
				string[] array5 = SplitParts(input);
				int[] array6 = new int[array5.Length - 1];
				for (int j = 1; j < array5.Length; j++)
				{
					string key = array5[j];
					if (!dictionary.TryGetValue(key, out var value))
					{
						string[] array7 = key.Split('/');
						int index = int.Parse(array7[0]) - 1;
						int num2 = ((array7.Length > 1 && array7[1].Length > 0) ? (int.Parse(array7[1]) - 1) : (-1));
						int num3 = ((array7.Length > 2 && array7[2].Length > 0) ? (int.Parse(array7[2]) - 1) : (-1));
						list4.Add(list[index]);
						list6.Add((num2 >= 0 && num2 < list3.Count) ? list3[num2] : Vector2.zero);
						value = num;
						dictionary.Add(key, value);
						num++;
					}
					array6[j - 1] = value;
				}
				for (int k = 1; k + 1 < array6.Length; k++)
				{
					list7.Add(array6[0]);
					list7.Add(array6[k]);
					list7.Add(array6[k + 1]);
				}
			}
		}
		Mesh mesh = new Mesh();
		mesh.SetVertices(list4);
		mesh.SetUVs(0, list6);
		mesh.SetTriangles(list7, 0);
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private static string[] SplitParts(string line)
	{
		return line.Split(' ');
	}

	private static float Parse(string s)
	{
		return float.Parse(s, CultureInfo.InvariantCulture);
	}

	private static Mesh CreateFallbackMesh()
	{
		GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
		Mesh sharedMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
		Object.Destroy(primitive);
		return sharedMesh;
	}
	}
}