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
using System.Text;
using System.Threading;
using System.IO.Ports;

namespace WidebandSupport
{
    public class LM1WidebandReader : IWidebandReader
    {

        private static BitPosition[] lambdaBitPositions = { 
                                                      new BitPosition(1, 1),
                                                      new BitPosition(2, 2),
                                                      new BitPosition(4, 4),
                                                      new BitPosition(8, 8),
                                                      new BitPosition(16, 16),
                                                      new BitPosition(32, 32),
                                                      new BitPosition(64, 64),
                                                      new BitPosition(256, 128),
                                                      new BitPosition(512, 256),
                                                      new BitPosition(1024, 512),
                                                      new BitPosition(2048, 1024),
                                                      new BitPosition(4096, 2048),
                                                      new BitPosition(8192, 4096) };

        private static BitPosition[] startStatusBitPositions = {     
                                                                new BitPosition(1024, 1),
                                                                new BitPosition(2048, 2),
                                                                new BitPosition(4096, 4),
                                                                new BitPosition(8192, 8)
                                                            };


        private static BitPosition[] startMultiplierBitPositions = {
                                                                new BitPosition(1, 1), 
                                                                new BitPosition(2, 2),
                                                                new BitPosition(4, 4),
                                                                new BitPosition(8, 8),
                                                                new BitPosition(16, 16),
                                                                new BitPosition(32, 32),
                                                                new BitPosition(64, 64),
                                                                new BitPosition(256, 128)
                                                                   };
        private Object locker = new Object();

        private double latestReading;
        public double LatestReading
        {
            get { return latestReading; }
        }

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

        public LM1WidebandReader(String comPortName)
        {

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
            comPort.BaudRate = 19200; // per iMFD 19200 baud
            comPort.DataBits = 8; // per iMFD 8
            comPort.Parity = Parity.None; // per iMFD N
            comPort.StopBits = StopBits.One; // per iMFD 1
            comPort.Handshake = Handshake.None;

        }

        /*
         * This method is only used for testing
         */
        private int GetByteFromSamplePacket()
        {
            byte[] packet = new byte[] {            
                (128 | 1), 
                (16 | 2 | 1), 
                ( 2 | 1 ), 
                ( 64 | 32 | 16 | 4 )
            };

            // FIRST WORD
            // byte[0] == "10000001"
            // byte[1] == "00010011"
            // start bit is set, multiplier is supposed to be 147

            // SECOND WORD (lambda Value is supposed to be 500)
            // byte[2] == "00000011"
            // byte[3] == "01110100"

            if (sampleBytePacketIndex >= packet.Length)
            {
                sampleBytePacketIndex = 0;
                Thread.Sleep(TimeSpan.FromMilliseconds(83));
            }

            return packet[sampleBytePacketIndex++];

        }

        private int GetLambda(int lambdaWord)
        {
            int lambdaValue = 0;

            foreach (BitPosition bp in lambdaBitPositions)
            {
                if ((lambdaWord & bp.Position) == bp.Position)
                {
                    lambdaValue |= bp.Value;
                }
            }

            return lambdaValue;
        }

        private int GetStatus(int startWord)
        {
            int status = 0;

            foreach (BitPosition bp in startStatusBitPositions)
            {
                if ((startWord & bp.Position) == bp.Position)
                {
                    status |= bp.Value;
                }
            }

            return status;
        }

        private int GetWord(byte[] wordBytes)
        {

            int wordValue;

            // swap bytes to little endian

            Array.Reverse(wordBytes);

            wordValue = BitConverter.ToUInt16(wordBytes, 0);

            return wordValue;

        }

        private int GetMultiplier(int startWord)
        {
            int multiplier = 0;

            foreach (BitPosition bp in startMultiplierBitPositions)
            {
                if ((startWord & bp.Position) == bp.Position)
                {
                    multiplier |= bp.Value;
                }
            }

            return multiplier;


        }


        private void InitiateReading()
        {
            List<byte> buffer = new List<byte>();

            bool packetStarted = false, normalOperation = false;
            int multiplier = 0;

            while (true == continueRunning)
            {
                try
                {
                    int aByte = 0;

                    if (true == testMode)
                    {
                        aByte = GetByteFromSamplePacket(); // test packet
                    }
                    else
                    {
                        aByte = comPort.ReadByte(); // to read from the serial port
                    }

                    if ((aByte & 128) == 128 && (aByte & 32) != 32 && (aByte & 2) != 2)
                    {
                        // last bit is set, 5th bit is 0, and 2nd bit is 0, must be start packet.
                        packetStarted = true;
                        buffer.Clear();
                        buffer.Add((byte)aByte);
                    }
                    else
                    {

                        // LM1 doesn't have a stop signal, the message can between 2 to 8 words.
                        // data consists of 16 packets (each packet is 16 bytes, a word)

                        if (true == packetStarted && buffer.Count <= (16 * 2))
                        {

                            buffer.Add((byte)aByte);

                            switch (buffer.Count)
                            {
                                case 2:
                                    byte[] startWordBytes = new byte[2] { buffer[0], buffer[1] };
                                    int startWord = GetWord(startWordBytes);
                                    int status = GetStatus(startWord);
                                    if (0 == status)
                                    {
                                        // 0000 is normal operation
                                        multiplier = GetMultiplier(startWord);
                                        normalOperation = true;
                                    }
                                    else
                                    {
                                        // 0001 Lambda value contains O2 level in 1/10%
                                        // 0010 Free air Calib in progress, Lambda data not valid
                                        // 0011 Need Free air Calibration request, Lambda data not valid
                                        // 0100 Warming up, Lambda value is temp in 1/10% of operating temp
                                        // 0101 Heater Calibration, Lambda value contains calibration countdown
                                        // 0110 Error code in Lambda value
                                        // 0111 Lambda Value is Flash level in 1/10%
                                        // 1xxx reserved

                                        if (normalOperation == true)
                                        {
                                            normalOperation = false;
                                            latestReading = 0;
                                        }
                                    }
                                    break;
                                case 4:
                                    if (normalOperation)
                                    {
                                        byte[] lambdaWordBytes = new byte[2] { buffer[2], buffer[3] };
                                        int lambdaWord = GetWord(lambdaWordBytes);
                                        latestReading = (GetLambda(lambdaWord) + 500) * multiplier / 10000d;
                                    }
                                    break;
                            }

                        }
                        else
                        {

                            // either packetStarted is false, or we have more words than expected
                            // either way, reset to safe values

                            packetStarted = false;
                            normalOperation = false;

                        }
                    }


                }

                catch (ThreadInterruptedException)
                {
                    // nothing
                }

            }

        }

        public void Start()
        {
            lock (locker)
            {
                if (worker == null || worker.IsAlive == false)
                {
                    continueRunning = true;
                    comPort.Open();
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

                    if (worker.IsAlive)
                    {
                        // if worker is still alive, most likely still blocked on readByte, interrupt
                        worker.Interrupt();
                    }

                    comPort.Close();

                }
                else
                {
                    throw new InvalidOperationException("Not started.");
                }
            }
        }

        class BitPosition
        {
            private int position;

            public int Position
            {
                get { return position; }
            }

            private int value;

            public int Value
            {
                get { return value; }
            }

            public BitPosition(int bitPosition, int bitValue)
            {
                this.position = bitPosition;
                this.value = bitValue;
            }
        }
    }

}
