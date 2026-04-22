#!/usr/bin/env python3
"""
Script to visualize depth images with proper colormap scaling.
This converts 16-bit depth images to a visible colormap.
"""
import rclpy
from rclpy.node import Node
from sensor_msgs.msg import Image
from cv_bridge import CvBridge
import cv2
import numpy as np

class DepthImageViewer(Node):
    def __init__(self):
        super().__init__('depth_image_viewer')
        self.bridge = CvBridge()
        
        # Subscribe to depth image
        self.subscription = self.create_subscription(
            Image,
            '/camera/camera/extrinsics/depth_to_color',
            
            #/camera/camera/depth/image_rect_raw
            self.depth_callback,
            10
        )
        
        self.get_logger().info('Subscribed to /camera/camera/depth/image_rect_raw')
        self.get_logger().info('Press Ctrl+C to exit')
    
    def depth_callback(self, msg):
        try:
            # Convert ROS Image message to OpenCV format (16-bit)
            depth_image = self.bridge.imgmsg_to_cv2(msg, "16UC1")
            
            # Convert from millimeters to meters (optional, for scaling)
            depth_meters = depth_image.astype(np.float32) / 1000.0
            
            # Method 1: Apply colormap for visualization
            # Scale to 0-255 range for colormap
            # Clamp values to reasonable range (0-5 meters)
            depth_clamped = np.clip(depth_meters, 0.0, 5.0)
            depth_normalized = (depth_clamped / 5.0 * 255).astype(np.uint8)
            
            # Apply colormap (JET colormap: blue=close, red=far)
            depth_colormap = cv2.applyColorMap(depth_normalized, cv2.COLORMAP_JET)
            
            # Set invalid pixels (0 depth) to black
            depth_colormap[depth_image == 0] = [0, 0, 0]
            
            # Display the colormapped depth image
            cv2.imshow("Depth Image (Colormap)", depth_colormap)
            
            # Also show raw depth as grayscale (scaled)
            depth_scaled = cv2.convertScaleAbs(depth_normalized)
            cv2.imshow("Depth Image (Grayscale Scaled)", depth_scaled)
            
            # Print some statistics
            valid_pixels = depth_image[depth_image > 0]
            if len(valid_pixels) > 0:
                self.get_logger().info(
                    f"Depth range: {valid_pixels.min()/1000:.2f}m - {valid_pixels.max()/1000:.2f}m, "
                    f"Mean: {valid_pixels.mean()/1000:.2f}m"
                )
            
            cv2.waitKey(1)
            
        except Exception as e:
            self.get_logger().error(f"Error processing depth image: {e}")

def main(args=None):
    rclpy.init(args=args)
    node = DepthImageViewer()
    
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        cv2.destroyAllWindows()
        node.destroy_node()
        rclpy.shutdown()

if __name__ == '__main__':
    main()
