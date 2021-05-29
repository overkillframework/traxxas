import sys
import struct
import socket
from rplidar import RPLidar

LIDAR_SERIAL_DEVICE = sys.argv[1]
UDP_SERVER_PORT = int(sys.argv[2])

def run():
    lidar = RPLidar(LIDAR_SERIAL_DEVICE)
    info = lidar.get_info()
    print(info)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        for measurement in lidar.iter_measurments():
            sock.sendto(struct.pack('<?iff', measurement[0], measurement[1], measurement[2], measurement[3]), ('localhost', UDP_SERVER_PORT))
    except KeyboardInterrupt:
        print('Stopping lidar')
    lidar.stop()
    lidar.stop_motor()
    lidar.disconnect()

if __name__ == '__main__':
    run()