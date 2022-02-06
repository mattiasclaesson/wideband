/*
 * Copyright 2021 Witold Olechowski
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO.Ports;
using System.IO;

namespace WidebandSupport
{
    public class LambdaWidebandReader : IWidebandReader
    {
        private Object locker = new Object();
        private double latestReading;
        public double LatestReading
        {
            get { return latestReading; }
        }

        private SerialPort comPort;
        private Thread worker;
        bool continueRunning = false;

        // test related bit
        int sampleLinePacketIndex = 0;
        private bool testMode = false; // if true, test mode
        public bool TestMode
        {
            get { return testMode; }
            set { testMode = value; }
        }

        readonly double[] lambdaConversion = new double[753] {
            0.750, 0.751, 0.752, 0.752, 0.753, 0.754, 0.755, 0.755, 0.756, 0.757,
            0.758, 0.758, 0.759, 0.760, 0.761, 0.761, 0.762, 0.763, 0.764, 0.764,
            0.765, 0.766, 0.766, 0.767, 0.768, 0.769, 0.769, 0.770, 0.771, 0.772,
            0.772, 0.773, 0.774, 0.774, 0.775, 0.776, 0.777, 0.777, 0.778, 0.779,
            0.780, 0.780, 0.781, 0.782, 0.782, 0.783, 0.784, 0.785, 0.785, 0.786,
            0.787, 0.787, 0.788, 0.789, 0.790, 0.790, 0.791, 0.792, 0.793, 0.793,
            0.794, 0.795, 0.796, 0.796, 0.797, 0.798, 0.799, 0.799, 0.800, 0.801,
            0.802, 0.802, 0.803, 0.804, 0.805, 0.805, 0.806, 0.807, 0.808, 0.808,
            0.809, 0.810, 0.811, 0.811, 0.812, 0.813, 0.814, 0.815, 0.815, 0.816,
            0.817, 0.818, 0.819, 0.820, 0.820, 0.821, 0.822, 0.823, 0.824, 0.825,
            0.825, 0.826, 0.827, 0.828, 0.829, 0.830, 0.830, 0.831, 0.832, 0.833,
            0.834, 0.835, 0.836, 0.837, 0.837, 0.838, 0.839, 0.840, 0.841, 0.842,
            0.843, 0.844, 0.845, 0.846, 0.846, 0.847, 0.848, 0.849, 0.850, 0.851,
            0.852, 0.853, 0.854, 0.855, 0.855, 0.856, 0.857, 0.858, 0.859, 0.860,
            0.861, 0.862, 0.863, 0.864, 0.865, 0.865, 0.866, 0.867, 0.868, 0.869,
            0.870, 0.871, 0.872, 0.873, 0.874, 0.875, 0.876, 0.877, 0.878, 0.878,
            0.879, 0.880, 0.881, 0.882, 0.883, 0.884, 0.885, 0.886, 0.887, 0.888,
            0.889, 0.890, 0.891, 0.892, 0.893, 0.894, 0.895, 0.896, 0.897, 0.898,
            0.899, 0.900, 0.901, 0.902, 0.903, 0.904, 0.905, 0.906, 0.907, 0.908,
            0.909, 0.910, 0.911, 0.912, 0.913, 0.915, 0.916, 0.917, 0.918, 0.919,
            0.920, 0.921, 0.922, 0.923, 0.924, 0.925, 0.926, 0.927, 0.928, 0.929,
            0.931, 0.932, 0.933, 0.934, 0.935, 0.936, 0.937, 0.938, 0.939, 0.940,
            0.941, 0.942, 0.944, 0.945, 0.946, 0.947, 0.948, 0.949, 0.950, 0.951,
            0.952, 0.953, 0.954, 0.955, 0.957, 0.958, 0.959, 0.960, 0.961, 0.962,
            0.963, 0.965, 0.966, 0.967, 0.969, 0.970, 0.971, 0.973, 0.974, 0.976,
            0.977, 0.979, 0.980, 0.982, 0.983, 0.985, 0.986, 0.987, 0.989, 0.990,
            0.991, 0.992, 0.994, 0.995, 0.996, 0.998, 0.999, 1.001, 1.003, 1.005,
            1.008, 1.010, 1.012, 1.015, 1.017, 1.019, 1.022, 1.024, 1.026, 1.028,
            1.030, 1.032, 1.035, 1.037, 1.039, 1.041, 1.043, 1.045, 1.048, 1.050,
            1.052, 1.055, 1.057, 1.060, 1.062, 1.064, 1.067, 1.069, 1.072, 1.075,
            1.077, 1.080, 1.082, 1.085, 1.087, 1.090, 1.092, 1.095, 1.098, 1.100,
            1.102, 1.105, 1.107, 1.110, 1.112, 1.115, 1.117, 1.120, 1.122, 1.124,
            1.127, 1.129, 1.132, 1.135, 1.137, 1.140, 1.142, 1.145, 1.148, 1.151,
            1.153, 1.156, 1.159, 1.162, 1.165, 1.167, 1.170, 1.173, 1.176, 1.179,
            1.182, 1.185, 1.188, 1.191, 1.194, 1.197, 1.200, 1.203, 1.206, 1.209,
            1.212, 1.215, 1.218, 1.221, 1.224, 1.227, 1.230, 1.234, 1.237, 1.240,
            1.243, 1.246, 1.250, 1.253, 1.256, 1.259, 1.262, 1.266, 1.269, 1.272,
            1.276, 1.279, 1.282, 1.286, 1.289, 1.292, 1.296, 1.299, 1.303, 1.306,
            1.310, 1.313, 1.317, 1.320, 1.324, 1.327, 1.331, 1.334, 1.338, 1.342,
            1.345, 1.349, 1.352, 1.356, 1.360, 1.364, 1.367, 1.371, 1.375, 1.379,
            1.382, 1.386, 1.390, 1.394, 1.398, 1.401, 1.405, 1.409, 1.413, 1.417,
            1.421, 1.425, 1.429, 1.433, 1.437, 1.441, 1.445, 1.449, 1.453, 1.457,
            1.462, 1.466, 1.470, 1.474, 1.478, 1.483, 1.487, 1.491, 1.495, 1.500,
            1.504, 1.508, 1.513, 1.517, 1.522, 1.526, 1.531, 1.535, 1.540, 1.544,
            1.549, 1.554, 1.558, 1.563, 1.568, 1.572, 1.577, 1.582, 1.587, 1.592,
            1.597, 1.601, 1.606, 1.611, 1.616, 1.621, 1.627, 1.632, 1.637, 1.642,
            1.647, 1.652, 1.658, 1.663, 1.668, 1.674, 1.679, 1.684, 1.690, 1.695,
            1.701, 1.707, 1.712, 1.718, 1.724, 1.729, 1.735, 1.741, 1.747, 1.753,
            1.759, 1.764, 1.770, 1.776, 1.783, 1.789, 1.795, 1.801, 1.807, 1.813,
            1.820, 1.826, 1.832, 1.839, 1.845, 1.852, 1.858, 1.865, 1.872, 1.878,
            1.885, 1.892, 1.898, 1.905, 1.912, 1.919, 1.926, 1.933, 1.940, 1.947,
            1.954, 1.961, 1.968, 1.975, 1.983, 1.990, 1.997, 2.005, 2.012, 2.020,
            2.027, 2.035, 2.042, 2.050, 2.058, 2.065, 2.073, 2.081, 2.089, 2.097,
            2.105, 2.113, 2.121, 2.129, 2.137, 2.145, 2.154, 2.162, 2.171, 2.179,
            2.188, 2.196, 2.205, 2.214, 2.222, 2.231, 2.240, 2.249, 2.258, 2.268,
            2.277, 2.286, 2.295, 2.305, 2.314, 2.324, 2.333, 2.343, 2.353, 2.363,
            2.373, 2.383, 2.393, 2.403, 2.413, 2.424, 2.434, 2.444, 2.455, 2.466,
            2.476, 2.487, 2.498, 2.509, 2.520, 2.532, 2.543, 2.554, 2.566, 2.577,
            2.589, 2.601, 2.613, 2.625, 2.637, 2.649, 2.662, 2.674, 2.687, 2.699,
            2.712, 2.725, 2.738, 2.751, 2.764, 2.778, 2.791, 2.805, 2.819, 2.833,
            2.847, 2.861, 2.875, 2.890, 2.904, 2.919, 2.934, 2.949, 2.964, 2.979,
            2.995, 3.010, 3.026, 3.042, 3.058, 3.074, 3.091, 3.107, 3.124, 3.141,
            3.158, 3.175, 3.192, 3.209, 3.227, 3.245, 3.263, 3.281, 3.299, 3.318,
            3.337, 3.355, 3.374, 3.394, 3.413, 3.433, 3.452, 3.472, 3.492, 3.513,
            3.533, 3.554, 3.575, 3.597, 3.618, 3.640, 3.662, 3.684, 3.707, 3.730,
            3.753, 3.776, 3.800, 3.824, 3.849, 3.873, 3.898, 3.924, 3.950, 3.976,
            4.002, 4.029, 4.056, 4.084, 4.112, 4.140, 4.169, 4.198, 4.228, 4.258,
            4.288, 4.319, 4.350, 4.382, 4.414, 4.447, 4.480, 4.514, 4.548, 4.583,
            4.618, 4.654, 4.690, 4.726, 4.764, 4.801, 4.840, 4.879, 4.918, 4.958,
            4.999, 5.040, 5.082, 5.124, 5.167, 5.211, 5.255, 5.299, 5.345, 5.391,
            5.438, 5.485, 5.533, 5.582, 5.632, 5.683 ,5.735, 5.788, 5.841, 5.896,
            5.953, 6.010, 6.069, 6.129, 6.190, 6.253, 6.318, 6.384, 6.452, 6.521,
            6.592, 6.665, 6.740, 6.817, 6.896, 6.976, 7.059, 7.144, 7.231, 7.320,
            7.412, 7.506, 7.602, 7.701, 7.803, 7.906, 8.013, 8.122, 8.234, 8.349,
            8.466, 8.587, 8.710, 8.837, 8.966, 9.099, 9.235, 9.374, 9.516, 9.662,
            9.811, 9.963, 10.119
        };

        public LambdaWidebandReader(String comPortName)
            : this(comPortName, false)
        {
        }

        public LambdaWidebandReader(String comPortName, bool isTestMode)
        {
            this.TestMode = isTestMode;
            if (TestMode)
            {
                return;
            }
            if (false == IsSerialPortNameValid(comPortName))
            {
                throw new ArgumentException(comPortName + ", is invalid.");
            }

            init(comPortName);
        }

        public void Dispose()
        {
            if (worker != null && worker.IsAlive && true == continueRunning)
            {
                Stop();
            }
        }

        private bool IsSerialPortNameValid(String comPortName)
        {
            bool serialPortNameValid = false;

            foreach (String serialPortName in SerialPort.GetPortNames())
            {
                if (serialPortName.Equals(comPortName))
                {
                    serialPortNameValid = true;
                }
            }

            return serialPortNameValid;
        }

        private void init(String comPortName)
        {
            comPort = new SerialPort();
            comPort.PortName = comPortName;
            comPort.BaudRate = 9600;
            comPort.DataBits = 8;
            comPort.Parity = Parity.None;
            comPort.StopBits = StopBits.One;
        }

        /*
         * This method is only used for testing
         */
        private String GetLineFromSamplePacket()
        {
            String[] lines = { "2,500,10495,500,2000,3000,16497",
                               "2,500,0,200,2000,3000,5702" };

            if (sampleLinePacketIndex >= lines.Length)
            {
                sampleLinePacketIndex = 0;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(10));

            return lines[sampleLinePacketIndex++];
        }

        private void InitiateReading()
        {
            try
            {
                if(!TestMode)
                {
                    comPort.Open();
                    comPort.DiscardInBuffer();
                }

                while (continueRunning)
                {

                    try
                    {
                        String line = null;

                        if (TestMode)
                        {
                            line = GetLineFromSamplePacket();
                        }
                        else
                        {
                            line = comPort.ReadLine();
                        }

                        ProcessData(line);
                    }
                    catch (ThreadInterruptedException)
                    {
                        // nothing
                    }
                }
            }
            catch (IOException)
            {
                // ignore
            }
            finally
            {
                comPort.Close();
            }
        }

        private void ProcessData(string line)
        {
            int HeaterStatus, hardwareId, CJ125_Status, adcValue_UA, adcValue_UR, adcValue_UB, crc;

            if (line == null)
            {
                Console.WriteLine("line is null");
                return;
            }

            String[] data = line.Split(',');
            if (data == null && data.Length != 7)
            {
                Console.WriteLine("data null or invalid lenght, raw: " + data);
                return;
            }

            int.TryParse(data[0], out HeaterStatus);
            int.TryParse(data[1], out hardwareId);
            int.TryParse(data[2], out CJ125_Status);
            int.TryParse(data[3], out adcValue_UA);
            int.TryParse(data[4], out adcValue_UR);
            int.TryParse(data[5], out adcValue_UB);
            int.TryParse(data[6], out crc);

            int calculatedCrc = HeaterStatus + hardwareId + CJ125_Status + 
                                adcValue_UA + adcValue_UR + adcValue_UB;
            if (crc != calculatedCrc)
            {
                Console.WriteLine("crc fail: " + crc + "!=" + calculatedCrc);
                return;
            }

            if(CJ125_Status != 0x28FF)
            {
                Console.WriteLine("CJ125_Status: " + CJ125_Status);
                return;
            }

            if (HeaterStatus == 2)
            {
                CalculateAfr(adcValue_UA);
            } else
            {
                //Console.WriteLine("HeaterStatus: " + HeaterStatus);
            }
        }

        private void CalculateAfr(int adcValue_UA)
        {
            double lambda = 0.0d;

            if (adcValue_UA > 791)
            {
                lambda = 10.119;
            }

            if (adcValue_UA < 39)
            {
                lambda = 0.750;
            }

            if (adcValue_UA >= 39 && adcValue_UA <= 791)
            {
                lambda = lambdaConversion[adcValue_UA - 39];
            }

            latestReading = lambda * 14.7;
        }

        public void Start()
        {
            lock (locker)
            {
                if (worker == null || worker.IsAlive == false)
                {
                    continueRunning = true;
                    worker = new Thread(new ThreadStart(InitiateReading));
                    worker.Start();
                }
                else
                {
                    throw new InvalidOperationException("Already started.");
                }
            }
        }

        public void Stop()
        {
            lock (locker)
            {
                if (continueRunning == true)
                {
                    continueRunning = false;
                    worker.Join(TimeSpan.FromSeconds(5));

                    if (worker.IsAlive)
                    {
                        // if worker is still alive, most likely still blocked on readByte, interrupt
                        worker.Interrupt();
                    }

                }
                else
                {
                    throw new InvalidOperationException("Not started.");
                }
            }
        }
    }
}
