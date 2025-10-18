using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;

public class learning01 : MonoBehaviour
{
    // Start is called before the first frame update

    public class Robot
    { 
        public Robot(string cs, int hp, int ap)
        {
            Callsign = cs;
            HitPoints = hp;
            ActionPoints = ap;
        }
        
        public string Callsign;
        public int HitPoints;
        public int ActionPoints;

        public void TakeDamage(int dmg) { HitPoints = HitPoints = Mathf.Max(0, HitPoints - dmg); }
    }
    void Start()
    {
        Robot Henry = new Robot("Red1", 100, 100);

        Debug.Log(Henry.Callsign);
        Debug.Log(Henry.HitPoints);
        Debug.Log(Henry.ActionPoints);

        Henry.TakeDamage(5);
        Debug.Log(Henry.HitPoints);
        Henry.TakeDamage(5);
        Debug.Log(Henry.HitPoints);
        Henry.TakeDamage(5);
        Debug.Log(Henry.HitPoints);
    }

    // Update is called once per frame
    void Update()
    {

    }
}
