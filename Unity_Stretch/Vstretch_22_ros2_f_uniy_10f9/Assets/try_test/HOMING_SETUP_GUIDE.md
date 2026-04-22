# Stretch 3 Robot Homing from Unity - Setup Guide

This guide explains how to home the physical Stretch 3 robot from Unity using ROS2 communication.

## Architecture Overview

```
Unity (HomeRobot.cs)
    │
    ├─→ Publishes to: /stretch/home_command (std_msgs/String)
    │
    └─→ Subscribes to: /stretch/home_status (std_msgs/String)
        │
        └─→ Receives: "homing_started", "homing_complete", "homing_failed: <error>"
```

**Robot Side:**
```
stretch_home_topic.py (on Stretch 3 robot)
    │
    ├─→ Subscribes to: /stretch/home_command
    │
    ├─→ Calls: stretch_body.robot.Robot().home()
    │
    └─→ Publishes to: /stretch/home_status
```

## What You Need to Run

### On Stretch 3 Robot:

1. **Python Script**: `stretch_home_topic.py`
   - Location: Copy `Assets/try_test/stretch_home_topic.py` to the robot
   - Run: `python3 stretch_home_topic.py`
   - This script:
     - Subscribes to `/stretch/home_command` topic
     - Calls `stretch_body.robot.Robot().home()` when command received
     - Publishes status to `/stretch/home_status` topic

2. **ROS2 Environment**:
   - Ensure ROS2 is sourced: `source /opt/ros/humble/setup.bash` (or your ROS2 distro)
   - Ensure `stretch_ros2` workspace is sourced if needed
   - Set ROS_DOMAIN_ID to match Unity (default: 0)

### On Unity:

1. **Script**: `HomeRobot.cs`
   - Already in your project at `Assets/try_test/HomeRobot.cs`
   - Attach to a GameObject in your scene
   - Ensure `ROS2UnityComponent` is in the scene

2. **Configuration**:
   - Set ROS_DOMAIN_ID in Unity to match robot (default: 0)
   - Configure ROS2UnityComponent with robot's IP if needed

## How It Works

### Execution Flow:

1. **Unity sends command**:
   ```csharp
   HomeRobot homeRobot = GetComponent<HomeRobot>();
   homeRobot.Home(); // Sends "home" message to /stretch/home_command
   ```

2. **Robot receives command**:
   - `stretch_home_topic.py` receives message on `/stretch/home_command`
   - Publishes "homing_started" to `/stretch/home_status`
   - Initializes robot if needed
   - Checks for runstop
   - Calls `robot.home()`

3. **Robot executes homing**:
   - Moves all joints to zero positions
   - Calibrates encoders
   - Takes ~30 seconds

4. **Robot sends status**:
   - On success: Publishes "homing_complete" to `/stretch/home_status`
   - On failure: Publishes "homing_failed: <error>" to `/stretch/home_status`

5. **Unity receives feedback**:
   - `HomeRobot.cs` receives status message
   - Updates internal state
   - Triggers `OnHomingComplete` or `OnHomingFailed` events
   - Updates `isHoming`, `lastHomingSuccess`, `lastHomingMessage` fields

## Setup Steps

### Step 1: Copy Script to Robot

```bash
# On your development machine
scp Assets/try_test/stretch_home_topic.py hello-robot@stretch3-ip:~/stretch_home_topic.py

# Or manually copy the file
```

### Step 2: Run Script on Robot

```bash
# SSH into Stretch 3 robot
ssh hello-robot@stretch3-ip

# Source ROS2
source /opt/ros/humble/setup.bash  # Adjust for your ROS2 distro

# Run the script
python3 ~/stretch_home_topic.py
```

You should see:
```
[INFO] [stretch_home_topic]: Stretch Home Topic subscriber started
[INFO] [stretch_home_topic]: Listening on /stretch/home_command
[INFO] [stretch_home_topic]: Publishing status to /stretch/home_status
```

### Step 3: Configure Unity

1. **Add HomeRobot script to scene**:
   - Create empty GameObject (or use existing)
   - Add `HomeRobot.cs` component
   - Ensure `ROS2UnityComponent` exists in scene

2. **Configure ROS2**:
   - Set ROS_DOMAIN_ID in ROS2UnityComponent to match robot
   - Verify network connectivity

3. **Test connection**:
   - Run Unity scene
   - Check Unity console for: `HomeRobot: Initialized`

### Step 4: Trigger Homing from Unity

**Option A: From Code**
```csharp
HomeRobot homeRobot = GetComponent<HomeRobot>();
homeRobot.Home();
```

**Option B: From UI Button**
```csharp
public HomeRobot homeRobot;

public void OnHomeButtonClicked()
{
    if (homeRobot != null)
    {
        homeRobot.Home();
    }
}
```

**Option C: Subscribe to Events**
```csharp
void Start()
{
    HomeRobot homeRobot = GetComponent<HomeRobot>();
    homeRobot.OnHomingComplete += (success, message) => {
        Debug.Log($"Homing complete: {success}, {message}");
    };
    homeRobot.OnHomingFailed += (error) => {
        Debug.LogError($"Homing failed: {error}");
    };
}
```

## Status Messages

The robot publishes these status messages to `/stretch/home_status`:

- `"homing_started"` - Homing sequence has begun
- `"homing_complete"` - Homing completed successfully
- `"homing_failed: <error message>"` - Homing failed with error

## Error Handling

### Common Issues:

1. **"Service not available"**:
   - Check if `stretch_home_topic.py` is running on robot
   - Verify ROS2 network connectivity
   - Check ROS_DOMAIN_ID matches

2. **"Timeout: No status received"**:
   - Robot script may have crashed
   - Check robot terminal for errors
   - Verify topic names match

3. **"Cannot home while run-stopped"**:
   - Physical or software runstop is active
   - Clear runstop on robot first

4. **"Failed to startup robot hardware"**:
   - Robot hardware issue
   - Check robot logs
   - Verify robot is powered on

## Alternative: Service-Based Approach

If you prefer ROS2 services (more standard), use `stretch_home_service.py` instead:

**Service Details:**
- Service Name: `/stretch/home`
- Service Type: `std_srvs/srv/Trigger`
- Request: Empty
- Response: `success` (bool), `message` (string)

**Note**: Service approach requires C# bindings for `std_srvs/srv/Trigger` in ROS2ForUnity. The topic-based approach is simpler and works with existing message types.

## Safety Notes

⚠️ **Important Safety Considerations:**

1. **Runstop Check**: The script checks for runstop before homing. If runstop is active, homing will fail.

2. **Single Homing**: Only one homing operation can run at a time. Additional requests are rejected.

3. **Robot State**: Unity should never assume homing succeeded. Always wait for status feedback.

4. **Timeout**: Homing has a timeout (default 60 seconds). If no status is received, Unity will report timeout.

5. **Robot Authority**: The robot is the source of truth. Unity only reflects what the robot reports.

## Testing

### Test on Robot First:

```bash
# On robot, test the script manually
ros2 topic pub /stretch/home_command std_msgs/msg/String "data: 'home'" --once

# Check status
ros2 topic echo /stretch/home_status
```

### Test from Unity:

1. Run Unity scene
2. Call `Home()` method
3. Watch Unity console for status updates
4. Verify robot actually homes

## Integration with Existing Code

This follows the same pattern as `unity_lift.py`:
- Topic-based commands
- Status feedback via topics
- Simple message types (std_msgs/String)

You can run both `unity_lift.py` and `stretch_home_topic.py` simultaneously on the robot.

## Next Steps

After homing works:
1. Add UI button to trigger homing
2. Subscribe to status events for visual feedback
3. Update robot state machine in Unity based on homing status
4. Add runstop status monitoring (subscribe to robot's runstop topic)

## References

- `stretch_body` documentation: https://docs.hello-robot.com/0.3/stretch-body/
- `stretch_ros2` repository: https://github.com/hello-robot/stretch_ros2
- `stretch_robot_home.py` source: https://github.com/hello-robot/stretch_body/blob/master/tools/bin/stretch_robot_home.py
