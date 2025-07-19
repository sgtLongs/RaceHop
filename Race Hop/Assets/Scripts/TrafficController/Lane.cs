using System.Collections.Generic;
using UnityEngine;

public class Lane : MonoBehaviour
{
	public Transform startPosition;
	public Transform endPosition;

	public List<Car> cars = new List<Car>();

	/// <summary>
	/// Add car to this lane if not already present.
	/// </summary>
	public void SubscribeCar(Car car)
	{
		if (car != null && !cars.Contains(car))
		{
			cars.Add(car);
		}
	}

	/// <summary>
	/// Remove car from this lane if present.
	/// </summary>
	public void UnsubscribeCar(Car car)
	{
		if (car != null && cars.Contains(car))
		{
			cars.Remove(car);
		}
	}
}
