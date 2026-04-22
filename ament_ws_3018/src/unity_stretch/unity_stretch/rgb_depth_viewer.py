#!/usr/bin/env python3
"""
ROS2 viewer: RGB + Depth from D435i.
Topics (Stretch / camera namespace):
  - /camera/camera/color/image_raw
  - /camera/camera/depth/image_rect_raw
"""

import rclpy
from rclpy.node import Node
from sensor_msgs.msg import Image
from cv_bridge import CvBridge
import cv2
import numpy as np


# Topic names matching your robot
RGB_TOPIC = "/camera/camera/color/image_raw"
DEPTH_TOPIC = "/camera/camera/depth/image_rect_raw"

# Depth display range (mm); adjust if your scene is closer/farther
DEPTH_DISPLAY_NEAR = 200   # mm
DEPTH_DISPLAY_FAR = 5000   # mm (5 m)


class RGBDepthViewer(Node):
    def __init__(self):
        super().__init__("rgb_depth_viewer")
        self.bridge = CvBridge()
        self.latest_rgb = None
        self.latest_depth = None

        self.rgb_sub = self.create_subscription(
            Image, RGB_TOPIC, self.rgb_cb, 10
        )
        self.depth_sub = self.create_subscription(
            Image, DEPTH_TOPIC, self.depth_cb, 10
        )
        self.timer = self.create_timer(0.033, self.show)  # ~30 Hz

    def rgb_cb(self, msg):
        try:
            img = self.bridge.imgmsg_to_cv2(msg, desired_encoding="passthrough")
            if len(img.shape) == 2:
                self.latest_rgb = cv2.cvtColor(img, cv2.COLOR_GRAY2BGR)
            else:
                # ROS uses RGB; OpenCV uses BGR for display
                self.latest_rgb = cv2.cvtColor(img, cv2.COLOR_RGB2BGR)
        except Exception as e:
            self.get_logger().warn(f"RGB callback: {e}")

    def depth_cb(self, msg):
        try:
            self.latest_depth = self.bridge.imgmsg_to_cv2(
                msg, desired_encoding="passthrough"
            )
        except Exception as e:
            self.get_logger().warn(f"Depth callback: {e}")

    def show(self):
        if self.latest_rgb is None and self.latest_depth is None:
            return

        h_display = 480
        frames = []

        # RGB
        if self.latest_rgb is not None:
            r = self.latest_rgb
            if r.shape[0] != h_display:
                r = cv2.resize(
                    r,
                    (int(r.shape[1] * h_display / r.shape[0]), h_display),
                    interpolation=cv2.INTER_LINEAR,
                )
            r_label = cv2.putText(
                r.copy(), "RGB", (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2
            )
            frames.append(r_label)

        # Depth (16-bit mm -> colormap)
        if self.latest_depth is not None:
            d = self.latest_depth.astype(np.float32)
            # Clip to display range and normalize
            d_clip = np.clip(d, DEPTH_DISPLAY_NEAR, DEPTH_DISPLAY_FAR)
            d_norm = (d_clip - DEPTH_DISPLAY_NEAR) / (
                DEPTH_DISPLAY_FAR - DEPTH_DISPLAY_NEAR + 1e-6
            )
            d_norm = (d_norm * 255).astype(np.uint8)
            depth_display = cv2.applyColorMap(d_norm, cv2.COLORMAP_JET)
            depth_display[d == 0] = [0, 0, 0]  # invalid = black

            if depth_display.shape[0] != h_display:
                depth_display = cv2.resize(
                    depth_display,
                    (int(depth_display.shape[1] * h_display / depth_display.shape[0]), h_display),
                    interpolation=cv2.INTER_NEAREST,
                )
            depth_label = cv2.putText(
                depth_display.copy(), "Depth (mm)", (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 255), 2
            )
            frames.append(depth_label)

        if not frames:
            return

        combined = np.hstack(frames)
        cv2.imshow("RGB | Depth (D435i)", combined)
        cv2.waitKey(1)


def main():
    rclpy.init()
    node = RGBDepthViewer()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    cv2.destroyAllWindows()
    node.destroy_node()
    rclpy.shutdown()


if __name__ == "__main__":
    main()
