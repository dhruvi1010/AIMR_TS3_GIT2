#!/usr/bin/env python3
"""
Read Stretch head pan/tilt directly from hardware without ROS driver.
This uses the Stretch Body API to query motor positions directly.
"""
import sys
import time

try:
    import stretch_body.robot as robot
except ImportError:
    print("ERROR: stretch_body not installed!")
    print("Install with: pip3 install hello-robot-stretch-body")
    sys.exit(1)

class HeadStateMonitor:
    def __init__(self):
        print("🤖 Initializing Stretch robot connection...")
        self.robot = robot.Robot()
        
        if not self.robot.startup():
            print(" Failed to connect to Stretch hardware!")
            print("Make sure:")
            print("  1. You're on the Stretch robot (not a remote PC)")
            print("  2. No other programs are using the robot")
            print("  3. You have permission to access /dev/hello-* devices")
            sys.exit(1)
        
        print(" Connected to Stretch hardware!")
        print(" Reading head state directly from motors...")
        print(" Manually move the head to see live updates\n")
        
    def read_head_state(self):
        """Read current head pan and tilt from hardware"""
        # Update status from hardware
        self.robot.head.pull_status()
        
        # Get pan (yaw) position in radians
        pan_rad = self.robot.head.motors['head_pan'].status['pos']
        
        # Get tilt position in radians  
        tilt_rad = self.robot.head.motors['head_tilt'].status['pos']
        
        return pan_rad, tilt_rad
    
    def monitor_continuous(self, rate_hz=10):
        """Continuously monitor and print head state"""
        period = 1.0 / rate_hz
        last_pan, last_tilt = None, None
        
        try:
            while True:
                pan, tilt = self.read_head_state()
                
                # Only print if changed significantly
                if (last_pan is None or 
                    abs(pan - last_pan) > 0.01 or 
                    abs(tilt - last_tilt) > 0.01):
                    
                    print(f" Head: pan={pan:+.3f} rad ({pan*57.3:+6.1f}°)  "
                          f"tilt={tilt:+.3f} rad ({tilt*57.3:+6.1f}°)")
                    
                    last_pan = pan
                    last_tilt = tilt
                
                time.sleep(period)
                
        except KeyboardInterrupt:
            print("\n\n👋 Shutting down...")
        finally:
            self.robot.stop()
    
    def read_once(self):
        """Read and print state once"""
        pan, tilt = self.read_head_state()
        print(f"\n Current Head State:")
        print(f"  Pan:  {pan:+.3f} rad ({pan*57.3:+6.1f}°)")
        print(f"  Tilt: {tilt:+.3f} rad ({tilt*57.3:+6.1f}°)\n")
        self.robot.stop()

def main():
    import argparse
    
    parser = argparse.ArgumentParser(
        description='Read Stretch head state directly from hardware (no driver needed)'
    )
    parser.add_argument(
        '--once', 
        action='store_true',
        help='Read once and exit (default: continuous monitoring)'
    )
    parser.add_argument(
        '--rate', 
        type=float, 
        default=10.0,
        help='Update rate in Hz for continuous mode (default: 10)'
    )
    
    args = parser.parse_args()
    
    monitor = HeadStateMonitor()
    
    if args.once:
        monitor.read_once()
    else:
        print(f"Monitoring at {args.rate} Hz (Ctrl+C to stop)\n")
        monitor.monitor_continuous(rate_hz=args.rate)

if __name__ == '__main__':
    main()