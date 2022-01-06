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
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace WidebandSupport
{
    public class STAGWidebandReader : IWidebandReader
    {

        private readonly Object locker = new Object();
        private readonly fuelTypeFunction fuelCalcFunction;

        private double latestReading;
        public double LatestReading
        {
            get { return latestReading; }
        }

        private SerialPort comPort;
        private Thread worker;
        bool continueRunning = false;

        int sampleBytePacketIndex = 0; // this is only used for testing.
        private bool testMode = false; // if true, test mode
        public bool TestMode
        {
            get { return testMode; }
            set { testMode = value; }
        }

        public STAGWidebandReader(String comPortName)
            : this(comPortName, false, new fuelTypeFunction(new FuelType().Gasoline))
        {
        }

        public void Dispose()
        {
            if (worker != null && worker.IsAlive && true == continueRunning)
            {
                Stop();
            }

        }

        public STAGWidebandReader(String comPortName, bool testmode, fuelTypeFunction fuelType)
        {
            this.TestMode = testmode;
            this.fuelCalcFunction = fuelType;

            if (false == IsSerialPortNameValid(comPortName))
            {
                throw new ArgumentException("com port: " + comPortName + ", is invalid.");
            }

            init(comPortName);
        }

        private bool IsSerialPortNameValid(String comPortName)
        {

            bool serialPortNameValid = false;

            foreach (String serialPortName in SerialPort.GetPortNames())
            {
                if (true == serialPortName.Equals(comPortName))
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
            comPort.BaudRate = 57600;
            comPort.DataBits = 8;
            comPort.Parity = Parity.None;
            comPort.StopBits = StopBits.One;
            comPort.Handshake = Handshake.None;

        }

        /*
         * This method is only used for testing
         */
        private byte GetByteFromSamplePacket()
        {                     // status ok
            byte[] packet = { 0x32, 0x00, 0x00, 0x1F, 0xE4, 0x00, 0x02, 0x00,
                              0x00, 0x09, 0x83, 0xAE, 0x00, 0x00, 0x9D, 0x24,
                              0x00, 0xC2, 0x00, 0x0A, 0x6A, 0x00, 0x41, 0x11,
                              0x00, 0x04, 0x00, 0x00, 0x00, 0x78, 0x01, 0x2C,
                              0x01, 0xF4, 0x58 };
            if (sampleBytePacketIndex >= packet.Length)
            {
                sampleBytePacketIndex = 0;
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }

            return packet[sampleBytePacketIndex++];

        }

        private void GetAfr(List<byte> p)
        {
            switch(p[6])
            {
                case 0x00:
                    //Console.WriteLine("status_sleep");
                    break;
                case 0x01:
                    //Console.WriteLine("status_warming");
                    break;
                case 0x02:
                    //Console.WriteLine("status_work");
                    double lambda = ((p[12] << 24)
                                    | (p[13] << 16)
                                    | (p[14] << 8)
                                    | p[15]) * 0.001d;
                    latestReading = fuelCalcFunction(lambda);
                    break;
                case 0x03:
                    //Console.WriteLine("status_breakdown");
                    break;
                default:
                    break;
            }
        }

        private void SendRequest(byte[] data)
        {
            if(this.TestMode || comPort == null)
            {
                return;
            }
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
            comPort.Write(data, 0, data.Length);
        }

        private void ProcessPacket(List<byte> packetContentBuffer)
        {
            switch (packetContentBuffer[4])
            {
                case 0x80:
                    SendRequest(new byte[] { 0x32, 0x00, 0x00, 0x03, 0x03, 0x00, 0x38 });
                    break;
                case 0x83:
                    SendRequest(new byte[] { 0x32, 0x00, 0x00, 0x03, 0x6D, 0x00, 0xA2 });
                    break;
                case 0xF0:
                    SendRequest(new byte[] { 0x32, 0x00, 0x00, 0x03, 0x64, 0x00, 0x99});
                    break;
                case 0xE4:
                    GetAfr(packetContentBuffer);
                    SendRequest(new byte[] { 0x32, 0x00, 0x00, 0x03, 0x64, 0x00, 0x99 });
                    break;
                default:
                    //Console.WriteLine("Not handled: " + packetContentBuffer[4].ToString("x"));
                    break;
            }
        }

        private void InitiateReading()
        {

            List<byte> packetContentBuffer = new List<byte>();
            bool packetStarted = false;
            int byteCounter = 0;
            int packetSize = 0;

            try
            {
                //initialise STAG AFR.
                if (!this.TestMode) {
                    comPort.Open();
                    SendRequest(new byte[] { 0xAC, 0x00, 0x00, 0x04, 0x00, 0x00, 0x32, 0xE2 });
                }
                while (true == continueRunning)
                {
                    try
                    {
                        byte aByte = 0;

                        if (TestMode)
                        {
                            aByte = GetByteFromSamplePacket(); // test packet
                        }
                        else
                        {
                            aByte = (byte)comPort.ReadByte(); // to read from the serial port
                        }

                        if (!packetStarted && aByte == 0x32)
                        {
                            packetContentBuffer.Clear();
                            packetContentBuffer.Add(aByte);
                            packetStarted = true;
                            byteCounter = 1;
                        }
                        else
                        {
                            packetContentBuffer.Add(aByte);
                            if (byteCounter++ == 3)
                            {
                                packetSize = aByte + 4;
                            }
                            if (packetSize == byteCounter)
                            {
                                packetStarted = false;
                                ProcessPacket(packetContentBuffer);
                            }

                        }

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

        public void Start()
        {
            lock (locker)
            {
                if (null == worker || false == worker.IsAlive)
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

                if (true == continueRunning)
                {
                    continueRunning = false;
                    worker.Join(TimeSpan.FromSeconds(5));

                    if (true == worker.IsAlive)
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

        public class FuelType
        {

            public double Lambda(double x)
            {
                return x;
            }

            public double Gasoline(double x)
            {
                return x * 14.7;
            }

            public double Diesel(double x)
            {
                return x * 50.0;
            }

            public double E10(double x)
            {
                return x * 14.1;
            }

            public double E85(double x)
            {
                return x * 9.7;
            }

            public double LPG(double x)
            {
                return x * 15.5;
            }

            public double CNG(double x)
            {
                return x * 17.2;
            }
        }
        public delegate double fuelTypeFunction(double x);
    }
}
