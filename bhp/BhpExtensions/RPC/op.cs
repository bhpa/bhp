using System;
using System.Text;
using Bhp.VM;

namespace Bhp.BhpExtensions.RPC
{
    public class Op
    {
        public UInt16 addr;
        public bool error;
        public OpCode code;
        public byte[] paramData;
        public ParamType paramType;
        public override string ToString()
        {
            var name = getCodeName();
            if (paramType == ParamType.None)
            {

            }
            else if (paramType == ParamType.ByteArray)
            {
                name += "[" + AsHexString() + "]";
            }
            else if (paramType == ParamType.String)
            {
                name += "[" + AsString() + "]";
            }
            else if (paramType == ParamType.Addr)
            {
                name += "[" + AsAddr() + "]";
            }
            return addr.ToString("x04") + ":" + name;
        }

        public string AsHexString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("0x");
            foreach (var b in paramData)
            {
                sb.Append(b.ToString("x02"));
            }
            return sb.ToString();
        }

        public string AsString()
        {
            return System.Text.Encoding.ASCII.GetString(paramData);
        }

        public Int16 AsAddr()
        {
            return BitConverter.ToInt16(paramData, 0);
        }

        public string getCodeName()
        {
            var name = "";
            if (this.error)
                name = "[E]";
            if (this.code == OpCode.PUSHT)
                return "PUSH1(true)";
            if (this.code == OpCode.PUSHF)
                return "PUSH0(false)";
            if (this.code > OpCode.PUSHBYTES1 && this.code < OpCode.PUSHBYTES75)
                return name + "PUSHBYTES" + (this.code - OpCode.PUSHBYTES1 + 1);
            else
                return name + this.code.ToString();
        }
    }

    public enum ParamType
    {
        None,
        ByteArray,
        String,
        Addr,
    }
}
