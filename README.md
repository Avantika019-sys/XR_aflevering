## **AR-Dart**
To run the application, you will need:
- Microsoft HoloLens 2
- Unity (2020.3 LTS and make sure to download Universal Windows Plattform and IL2CPP)
- Microsoft Mixed Reality Toolkit
- A Visual Studio version (2019 or higher).

### **NOTE**
This project implementation for the course SWIGX, AI-assisted tools such as GitHub Copilot was used for code suggestions and debugging support during the project development.


### **Setup**

Clone the git-repository and open the Mixed Reality Toolkit. Use the button **Restore Features** to get all the packages you need to run the game. Afterwards open the project in Unity. Once you are in Unity, under **File** go to **Build Settings**. Change the output device to Universal Windows Plattform UWP, and press the button **build** without the run option, choose the build folder. Once builded, in the build folder there is an **ar-dart.sln solution file**. Open the file, and Visual Studio should open up. In Visual Studio, change output source to Device and use **Run without Debugging** to run the application and flash it onto the HoloLens 2. We suggest to flash it by cable, as it's much faster.

### **The game**

In the HoloLens 2, there should be an application ar-dart now.
