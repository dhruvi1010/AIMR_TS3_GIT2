using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class SimpleTcpTest : MonoBehaviour
{
    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>("/unity_test");

        // Try to publish a test message
        InvokeRepeating("PublishTest", 1f, 1f);

        Debug.Log("ROS Connection established, publishing to /unity_test");
    }

    void PublishTest()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        var msg = new StringMsg { data = "Hello from Unity at " + Time.time };
        ros.Publish("/unity_test", msg);
        Debug.Log("Published test message");
    }
}