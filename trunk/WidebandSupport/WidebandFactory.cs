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

    public class WidebandFactory
    {
        private String deviceType;
        private String comPort;
        private bool testMode;

        public WidebandFactory()
        {
            LoadConfigFromFile(null);
        }

        public WidebandFactory(String deviceType, String comPort, bool testMode)
        {
            this.deviceType = deviceType;
            this.comPort = comPort;
            this.testMode = testMode;
        }

        private void LoadConfigFromFile(String filePath)
        {
            FileStream fileStream;

            if (filePath == null)
            {
                filePath = "wb.config";
            }

            if (File.Exists(filePath))
            {
                fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

                using (StreamReader reader = new StreamReader(fileStream))
                {

                    String line;

                    char[] splitChars = new char[] { ':' };

                    while ((line = reader.ReadLine()) != null)
                    {

                        if (false == line.Trim().StartsWith("#") && line.Contains(":"))
                        {
                            String[] args = line.Split(splitChars);
                            this.deviceType = args[0].Trim();
                            this.comPort = args[1].Trim();
                            this.testMode = Boolean.Parse(args[2]);
                            break;
                        }

                    }

                }

                fileStream.Close();

            }
            else
            {
                throw new ArgumentException("Could not load wideband config file, path: " + filePath);
            }

        }

        public IWidebandReader CreateInstance()
        {

            IWidebandReader reader = null;

            switch (deviceType)
            {
                case "PLX":
                    reader = new PLXWidebandReader(comPort);
                    break;
                case "LM1":
                    reader = new LM1WidebandReader(comPort);
                    break;
                case "LC1":
                    reader = new LC1WidebandReader(comPort);
                    break;
                case "ZT2":
                    reader = new ZT2WidebandReader(comPort);
                    break;
                case "AEM":
                    reader = new AEMWidebandReader(comPort);
                    break;
                case "NOOP":
                    reader = new NOOPWidebandReader();
                    break;
                default:
                    throw new ArgumentException(deviceType + " is not supported.");
            }

            if (reader != null)
            {
                reader.TestMode = testMode;
            }

            return reader;

        }

    }
}

