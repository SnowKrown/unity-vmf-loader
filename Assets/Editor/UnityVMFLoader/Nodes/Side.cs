using UnityEngine;
using TriangleNet;
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;

using Mesh = UnityEngine.Mesh;

namespace UnityVMFLoader.Nodes
{
	public class Side : Node
	{
		public Plane Plane;

		public Vector3 PointA;
		public Vector3 PointB;
		public Vector3 PointC;

		public Vector3 Center;

		public string Material;

		public Vector3 UAxis;
		public float UAxisTranslation;
		public float UAxisScale;

		public Vector3 VAxis;
		public float VAxisTranslation;
		public float VAxisScale;

		public float Rotation;

		private const float inchesInMeters = 0.0254f;

		private static readonly Regex planeRegex;
		private static readonly Regex uvRegex;

		static Side()
		{
			// (-98.0334 356.145 -1.90735e-006) (-98.0334 356.145 0.999998) (-122 334.941 0.999998)

			planeRegex = new Regex(@"\((.+?) (.+?) (.+?)\) \((.+?) (.+?) (.+?)\) \((.+?) (.+?) (.+?)\)", RegexOptions.Compiled);

			// [0 -1 0 384] 0.25

			uvRegex = new Regex(@"\[(.+?) (.+?) (.+?) (.+?)\] (.+)", RegexOptions.Compiled);
		}

		public override void Parse(string key, string value)
		{
			base.Parse(key, value);

			Match match;

			switch (key)
			{
				case "plane":

					match = planeRegex.Match(value);

					if (!match.Success)
					{
						throw new Exception("Failed to match a plane on side " + Identifier + ".");
					}

					PointA = new Vector3
					(
						float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)
					);

					PointB = new Vector3
					(
						float.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture)
					);

					PointC = new Vector3
					(
						float.Parse(match.Groups[7].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[8].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[9].Value, CultureInfo.InvariantCulture)
					);

					if (PointA.magnitude > 16384 || PointB.magnitude > 16384 || PointC.magnitude > 16384)
					{
						Debug.LogWarning(String.Format("An initial point is really far away on side {0}.", Identifier));
					}

					// Scale from inches to meters.

					PointA *= inchesInMeters;
					PointB *= inchesInMeters;
					PointC *= inchesInMeters;

					// Flip Z and Y axises since they mean different things in Source and Unity.

					var y = PointA.y;

					PointA.y = PointA.z;
					PointA.z = y;

					y = PointB.y;

					PointB.y = PointB.z;
					PointB.z = y;

					y = PointC.y;

					PointC.y = PointC.z;
					PointC.z = y;

					Plane = new Plane(PointA, PointB, PointC);

					Center = (PointA + PointB + PointC) / 3;

					break;

				case "material":

					Material = value;

					break;

				case "uaxis":

					match = uvRegex.Match(value);

					if (!match.Success)
					{
						throw new Exception("Failed to match 'uaxis' on side " + Identifier + ".");
					}

					UAxis = new Vector3
					(
						float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
					);

					UAxisTranslation = float.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
					UAxisScale = float.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) * inchesInMeters;

					break;

				case "vaxis":

					match = uvRegex.Match(value);

					if (!match.Success)
					{
						throw new Exception("Failed to match 'vaxis' on side " + Identifier + ".");
					}

					VAxis = new Vector3
					(
						float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
						float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
					);

					VAxisTranslation = float.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
					VAxisScale = float.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) * inchesInMeters;

					break;

				case "rotation":

					Rotation = float.Parse(value, CultureInfo.InvariantCulture);

					break;
			}
		}

		static public explicit operator Mesh(Side side)
		{
			// Calculate the intersection points of this plane with the other planes in this solid.

			var intersections = new List<Vector3>();

			var solid = (Solid) side.Parent;

			var sides = solid.Children.OfType<Side>();

			foreach (var side2 in sides)
			{
				foreach (var side3 in sides)
				{
					var intersection = GetPlaneIntersectionPoint(side.Plane, side2.Plane, side3.Plane);

					if (intersection == null)
					{
						continue;
					}

					if (intersections.Contains((Vector3) intersection, new Vector3NearEqualComparer()))
					{
						continue;
					}

					intersections.Add(((Vector3) intersection));
				}
			}

			// Discard points that can't be reached by a line from the center to the point.

			var newIntersections = new List<Vector3>();

			foreach (var point in intersections)
			{
				var discard = sides.Except(new [] {side}).Any
				(
					x =>
					{
						var ray = new Ray(side.Center, (point - side.Center).normalized);

						float distanceTravelled;

						var hit = x.Plane.Raycast(ray, out distanceTravelled);

						// Did it hit something before reaching its destination?

						return hit && distanceTravelled < Vector3.Distance(side.Center, point) - 0.01f;
					}
				);

				if (!discard)
				{
					newIntersections.Add(point);
				}
			}

			intersections = newIntersections;

			// Add the initial 3 points.

			intersections.Add(side.PointA);
			intersections.Add(side.PointB);
			intersections.Add(side.PointC);

			// Remove duplicate points.

			intersections = intersections.Distinct(new Vector3NearEqualComparer()).ToList();

			// Ignore the side if it's broken.

			if (intersections.Count() < 3)
			{
				Debug.LogError(String.Format("Only {0} intersections on side {1} - ignoring.", intersections.Count(), side.Identifier));

				return null;
			}

			// Transform the 3D polygon to a 2D polygon so that it can be triangulated.

			var angle = Vector3.Angle(Vector3.forward, side.Plane.normal);
			var axis = Vector3.Cross(side.Plane.normal.normalized, Vector3.forward);

			var intersections2D = intersections.Select(point => Quaternion.AngleAxis(angle, axis) * (point - side.Center)).ToList();

			// Triangulate the polygon.

			var input = new TriangleNet.Geometry.InputGeometry(intersections2D.Count());

			intersections2D.ForEach(point => input.AddPoint(point.x, point.y));

			var output = new TriangleNet.Mesh();

			output.Triangulate(input);

			var renderableOutput = new TriangleNet.MeshRenderer.Core.RenderData();

			renderableOutput.SetMesh(output);

			intersections2D = output.Vertices.Select(vertex => new Vector3((float) vertex.X, (float) vertex.Y, 0)).ToList();

			// Transform the triangulated polygon back to 3D.

			intersections = intersections2D.Select(point => Quaternion.AngleAxis(-angle, axis) * point + side.Center).ToList();

			// Create a mesh for it.

			var mesh = new Mesh();

			mesh.vertices = intersections.ToArray();
			mesh.triangles = renderableOutput.Triangles.Select(x => (int) x).ToArray();

			mesh.RecalculateNormals();
			mesh.RecalculateBounds();

			// Flip it if it's facing the wrong way.

			var center = sides.Average(x => x.Center);

			var direction = (side.Center - center).normalized;

			if (Vector3.Dot(mesh.normals[0], direction) < 0)
			{
				mesh.triangles = mesh.triangles.Reverse().ToArray();

				mesh.RecalculateNormals();
			}

			return mesh;
		}

		private class Vector3NearEqualComparer : IEqualityComparer<Vector3>
		{
			public bool Equals(Vector3 left, Vector3 right)
			{
				return Vector3.Distance(left, right) < 0.001f;
			}

			public int GetHashCode(Vector3 vector)
			{
				return string.Format("{0:0.000} {1:0.000} {2:0.000}", vector.x, vector.y, vector.z).GetHashCode();
			}
		}

		static private Vector3? GetPlaneIntersectionPoint(Plane plane1, Plane plane2, Plane plane3)
		{
			// Unity really should have a Matrix3x3 class. And a Determinant method.

			float determinant =
			(
				(
					plane1.normal.x * plane2.normal.y * plane3.normal.z +
					plane1.normal.y * plane2.normal.z * plane3.normal.x +
					plane1.normal.z * plane2.normal.x * plane3.normal.y
				)

				-

				(
					plane1.normal.z * plane2.normal.y * plane3.normal.x +
					plane1.normal.y * plane2.normal.x * plane3.normal.z +
					plane1.normal.x * plane2.normal.z * plane3.normal.y
				)
			);

			// Can't intersect parallel planes.

			if (determinant == 0.0f)
			{
				return null;
			}

			var point =
			(
				Vector3.Cross(plane2.normal, plane3.normal) * -plane1.distance +
				Vector3.Cross(plane3.normal, plane1.normal) * -plane2.distance +
				Vector3.Cross(plane1.normal, plane2.normal) * -plane3.distance
			)

			/

			determinant;

			if (point.magnitude > 450)
			{
				return null;
			}

			return point;
		}
	}
}
