# Create a simple test script to debug imports
# Save this as test_imports.py in your Python folder

import sys
print("Python version:", sys.version)
print("Python path:", sys.path)

try:
    import cv2
    print("✓ OpenCV imported successfully")
    print("OpenCV version:", cv2.__version__)
except ImportError as e:
    print("✗ OpenCV import failed:", e)

try:
    import face_recognition_models
    print("✓ face_recognition_models imported successfully")
    print("Models path:", face_recognition_models.face_recognition_model_location())
except ImportError as e:
    print("✗ face_recognition_models import failed:", e)

try:
    import face_recognition
    print("✓ face_recognition imported successfully")
except ImportError as e:
    print("✗ face_recognition import failed:", e)
    print("Error details:", str(e))