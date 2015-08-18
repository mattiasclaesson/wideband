/*
 * Copyright 2009 George Daswani
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
using System.Threading;
using System.IO.Ports;
using System.IO;

namespace WidebandSupport
{
    public class PLXWidebandReader : IWidebandReader
    {

        private Object locker = new Object();
        private fuelTypeFunction fuelCalcFunction;

        private double latestReading;
        public double LatestReading
        {
            get { return latestReading; }
        }

        private readonly int instanceNumber;
        private SerialPort comPort;
        private Thread worker;
        bool continueRunning = false;

        // test related bits

        int sampleBytePacketIndex = 0; // this is only used for testing.
        private bool testMode = false; // if true, test mode
        public bool TestMode
        {
            get { return testMode; }
            set { testMode = value; }
        }

        public PLXWidebandReader(String comPortName)
            : this(comPortName, 1, new fuelTypeFunction(new FuelType().Gasoline))
        {
        }

        public PLXWidebandReader(String comPortName, int instanceNumber)
            : this(comPortName, instanceNumber, new fuelTypeFunction(new FuelType().Gasoline))
        {
        }

        public void Dispose()
        {
            if (worker != null && worker.IsAlive && true == continueRunning)
            {
                Stop();
            }

        }

        public PLXWidebandReader(String comPortName, int instanceNumber, fuelTypeFunction fuelType)
        {

            if (false == IsSerialPortNameValid(comPortName))
            {
                throw new ArgumentException("com port: " + comPortName + ", is invalid.");
            }


            if (instanceNumber < 1 || instanceNumber > 32)
            {
                // Note: 1 should be passed if there's only one AFR module.
                throw new ArgumentOutOfRangeException("instance number is required to be between 1 and 32.");
            }

            init(comPortName);

            this.fuelCalcFunction = fuelType;
            this.instanceNumber = instanceNumber;
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
            comPort.BaudRate = 19200; // per iMFD 19200 baud
            comPort.DataBits = 8; // per iMFD 8
            comPort.Parity = Parity.None; // per iMFD N
            comPort.StopBits = StopBits.One; // per iMFD 1
            comPort.Handshake = Handshake.None;

        }

        /*
         * This method is only used for testing
         */
        private byte GetByteFromSamplePacket()
        {
            byte[] packet = { 0x80, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x01, 0x00, 0x05, 0x00, 0x01, 0x00, 0x00, 0x0F, 0x40 };

            if (sampleBytePacketIndex >= packet.Length)
            {
                sampleBytePacketIndex = 0;
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }

            return packet[sampleBytePacketIndex++];

        }

        private double GetDataFromPacket(List<byte> packetContentBuffer)
        {

            double afrReading = 0;

            for (int i = 1; (i + 5) < packetContentBuffer.Count; i = i + 5)
            {
                // Address MSB == 0 && Address LSB == 0 is a wideband.
                if (0 == packetContentBuffer[i + (int)PacketDataOffset.AddressMSB] && 0 == packetContentBuffer[i + (int)PacketDataOffset.AddressLSB])
                {
                    // instance number is ordinal, 0-based.  Need to add one before comparing.
                    if (instanceNumber == ((packetContentBuffer[i + (int)PacketDataOffset.Instance]) + 1))
                    {
                        // we found the correct instance number

                        int dataMSB = packetContentBuffer[i + (int)PacketDataOffset.DataMSB];
                        int dataLSB = packetContentBuffer[i + (int)PacketDataOffset.DataLSB];

                        afrReading = fuelCalcFunction((dataMSB << 6) | dataLSB);

                        break;
                    }
                }
            }

            return afrReading;
        }

        private void InitiateReading()
        {

            List<byte> packetContentBuffer = new List<byte>();
            bool packetStarted = false;

            try
            {
                comPort.Open();

                while (true == continueRunning)
                {

                    try
                    {

                        byte aByte = 0;

                        if (testMode)
                        {
                            aByte = GetByteFromSamplePacket(); // test packet
                        }
                        else
                        {
                            aByte = (byte)comPort.ReadByte(); // to read from the serial port
                        }

                        switch (aByte)
                        {
                            case 0x80:
                                // start byte
                                packetContentBuffer.Clear();
                                packetContentBuffer.Add(aByte);
                                packetStarted = true;
                                break;
                            case 0x40:
                                // stop byte
                                if (packetStarted)
                                {
                                    packetContentBuffer.Add(aByte);
                                    latestReading = GetDataFromPacket(packetContentBuffer);
                                    packetStarted = false;
                                }
                                break;
                            default:
                                if (packetStarted)
                                {
                                    packetContentBuffer.Add(aByte);
                                }
                                break;
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
                return (x / 3.75 + 68) / 100d;
            }

            public double Gasoline(double x)
            {
                return (x / 2.55 + 100) / 10d;
            }

            public double Diesel(double x)
            {
                return (x / 2.58 + 100) / 10d;
            }

            public double Methanol(double x)
            {
                return (x / 5.856 + 43.5) / 10d;
            }

            public double Ethanol(double x)
            {
                return (x / 4.167 + 61.7) / 10d;
            }

            public double LPG(double x)
            {
                return (x / 2.417 + 105.6) / 10d;
            }

            public double CNG(double x)
            {
                return (x / 2.18 + 117) / 10d;
            }
        }

        enum PacketDataOffset : int { AddressMSB = 0, AddressLSB = 1, Instance = 2, DataMSB = 3, DataLSB = 4 };

        public delegate double fuelTypeFunction(double x);
    }
}
