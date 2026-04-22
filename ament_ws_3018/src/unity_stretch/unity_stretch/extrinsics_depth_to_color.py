#!/usr/bin/env python3
"""
Print depth_to_color extrinsics from D435i (ROS2).
Topic: /camera/camera/extrinsics/depth_to_color
Message: realsense2_camera_msgs/msg/Extrinsics
"""

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, DurabilityPolicy, ReliabilityPolicy
from realsense2_camera_msgs.msg import Extrinsics


EXTRINSICS_TOPIC = "/camera/camera/extrinsics/depth_to_color"

# Camera publishes extrinsics once with TRANSIENT_LOCAL; we must match to receive it
EXTRINSICS_QOS = QoSProfile(
    depth=10,
    durability=DurabilityPolicy.TRANSIENT_LOCAL,
    reliability=ReliabilityPolicy.RELIABLE,
)


def print_extrinsics(msg):
    """Print Extrinsics (rotation 3x3 column-major, translation in meters)."""
    # rotation is float64[9] column-major 3x3
    r = msg.rotation
    print("--- Extrinsics: depth_to_color ---")
    print("Rotation (3x3, column-major):")
    print(f"  [{r[0]:+.6f}, {r[3]:+.6f}, {r[6]:+.6f}]")
    print(f"  [{r[1]:+.6f}, {r[4]:+.6f}, {r[7]:+.6f}]")
    print(f"  [{r[2]:+.6f}, {r[5]:+.6f}, {r[8]:+.6f}]")
    print("Translation (meters):")
    print(f"  x = {msg.translation[0]:+.6f}")
    print(f"  y = {msg.translation[1]:+.6f}")
    print(f"  z = {msg.translation[2]:+.6f}")
    print("---------------------------------")


class ExtrinsicsViewer(Node):
    def __init__(self, once=False):
        super().__init__("extrinsics_viewer")
        self.once = once
        self.done = False
        self.sub = self.create_subscription(
            Extrinsics,
            EXTRINSICS_TOPIC,
            self.callback,
            EXTRINSICS_QOS,
        )

    def callback(self, msg):
        print_extrinsics(msg)
        if self.once:
            self.done = True


def main():
    import sys
    rclpy.init()
    once = "--once" in sys.argv or "-1" in sys.argv
    node = ExtrinsicsViewer(once=once)
    print("Waiting for extrinsics on", EXTRINSICS_TOPIC, "...", flush=True)
    if once:
        while rclpy.ok() and not node.done:
            rclpy.spin_once(node, timeout_sec=0.1)
        if not node.done:
            print("No message received. Check camera node is running and topic exists.", flush=True)
    else:
        rclpy.spin(node)
    node.destroy_node()
    rclpy.shutdown()


if __name__ == "__main__":
    main()
