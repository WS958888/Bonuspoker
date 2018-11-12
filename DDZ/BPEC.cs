using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;

namespace BPEC
{
    public class BPEC : SmartContract
    {

        // CGAS合约hash
        delegate object deleDyncall(string method, object[] arr);

        // the owner, super admin address  
        public static readonly byte[] ContractOwner = "AUGkNMWzBCy5oi1rFKR5sPhpRjhhfgPhU2".ToScriptHash();

        [Serializable]
        /**
         *  交易流水记录 
         **/
        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public byte ttp; //交易类型:1-用户之间交易;2-购买股权解禁;3-分享股权解禁;4-社区股权解禁;5-团队股权解禁
            public BigInteger value;
        }

        /**
         *用户资产数据结构
         */
        public class Info
        {
            public BigInteger balance;//用户股权资产余额
            public BigInteger block;//股权数最后变动区块
        }

        /**
         *分红池数据结构
         */
        public class ClaimPoolInfo
        {
            public BigInteger claimCode;//分红池序号
            public BigInteger claimPool;//分红池CGAS
            public BigInteger pCount;//分红人数
            public BigInteger saleCount;//出售总数(股权)
            public BigInteger shareCount;//推广奖励总数(股权)
            public BigInteger teamCount;//团队解禁总数(股权)
            public BigInteger commCount;//社区解禁总数(股权)
            public byte[] buyAddress;//最后一个购买者，只有购买会刷新
            public byte claimState;//分红池状态:0-当前分红池;1-正在分红;2-已分红
        }

        /**
         * 股权余额数据结构
         */
        public class BPECPoolInfo
        {
            public BigInteger obpecBalance;//玩家挖矿股权余额
            public BigInteger tbpecBalance;//团队股权余额
            public BigInteger cbpecBalance;//社区股权余额
            public BigInteger totalBPEC;//解禁股权总数
            public BigInteger claimCode;//当前分红池序号
            public byte[] endBuyAddress;//最后一个购买者
            public byte obpecOver;//出售股权是否发完0-无;1-有
            public byte bpecState;//0-关闭;1-开启
        }

        /**
          *初始化配置数据结构
          */
        public class BPECInfo
        {
            public BigInteger oTotalSupply;//玩家挖矿股权总数
            public BigInteger tTotalSupply;//团队股权总数
            public BigInteger cTotalSupply;//社区股权总数
        }

        // notify 初始化合约通知
        public delegate void deleDeploy(byte state);
        [DisplayName("deploy")]
        public static event deleDeploy Deployed;

        //notify  交易记录
        public delegate void deleTransfer(byte state,byte[] from, byte[] to,byte ttp, BigInteger value, BigInteger claimId);
        [DisplayName("transfer")]
        public static event deleTransfer Transfered;

        //notify  解禁记录
        public delegate void deleTransferOpen(byte state, byte[] to, byte ttp, BigInteger gold, BigInteger value, byte obpecOver, BigInteger claimId);
        [DisplayName("transferOpen")]
        public static event deleTransferOpen TransferOpened;

        //notify  打开合约
        public delegate void deleOpenBPEC(byte state);
        [DisplayName("openBPEC")]
        public static event deleOpenBPEC OpenBPECed;

        //notify  打开合约
        public delegate void deleCloseBPEC(byte state);
        [DisplayName("closeBPEC")]
        public static event deleCloseBPEC CloseBPECed;

        //notify  开始分红
        public delegate void deleStartClaim(byte state,BigInteger claimId, BigInteger claimPool, BigInteger totalBPEC, BigInteger oClaimId);
        [DisplayName("startClaim")]
        public static event deleStartClaim StartClaimed;


        public static string name()
        {
            return "NEP5 Coin BPEC";
        }
        public static string symbol()
        {
            return "BPEC";
        }

        /**
         * 版本
         */
        public static string Version()
        {
            return "1.1.1";
        }

        private const ulong factor = 100;//精度2
        private const ulong OBPEC = 10 * 100000000 * factor;//外部发行量10亿
        private const ulong CBPEC = 4 * 100000000 * factor;//社区发行量4亿
        private const ulong TBPEC = 6 * 100000000 * factor;//团队发行量4亿
        public static byte decimals()
        {
            return 2;
        }

        //DDZ 发行总量查询
        public static BigInteger totalSupply(byte stype)
        {
            BigInteger coin = 0;
            BPECInfo bpecInfo = getBPECInfo();
            if(bpecInfo.oTotalSupply>0)
            {
                if (stype == 1)
                {
                    coin = bpecInfo.tTotalSupply;
                }
                else if (stype == 2)
                {
                    coin = bpecInfo.cTotalSupply;
                } else if (stype == 3)
                {
                    coin = bpecInfo.oTotalSupply;
                }
                else
                {
                    BigInteger tCoin = bpecInfo.tTotalSupply;
                    BigInteger cCoin = bpecInfo.cTotalSupply;
                    BigInteger oCoin = bpecInfo.oTotalSupply;
                    coin = tCoin + cCoin + oCoin;
                }
            }
            return coin;
        }

        /**
         * 初始化合约
         **/ 
        public static bool deploy()
        {
            bool bol = false;
            if (Runtime.CheckWitness(ContractOwner))
            {
               
                BPECInfo bpecInfo = getBPECInfo();
                if (bpecInfo.oTotalSupply==0)
                {
                    //基本配置
                    bpecInfo.oTotalSupply = OBPEC;
                    bpecInfo.tTotalSupply = TBPEC;
                    bpecInfo.cTotalSupply = CBPEC;
                    Storage.Put(Storage.CurrentContext, "BPECInfo", Helper.Serialize(bpecInfo));
                    //余额池
                    BPECPoolInfo bpecPoolInfo = getBPECPoolInfo();
                    bpecPoolInfo.obpecBalance = OBPEC;
                    bpecPoolInfo.tbpecBalance = TBPEC;
                    bpecPoolInfo.cbpecBalance = CBPEC;
                    bpecPoolInfo.obpecOver = 1;
                    bpecPoolInfo.claimCode = 1;
                    bpecPoolInfo.bpecState = 1;
                    Storage.Put(Storage.CurrentContext, "BPECPoolInfo", Helper.Serialize(bpecPoolInfo));
                    //初始化分红池
                    ClaimPoolInfo claimPoolInfo = new ClaimPoolInfo();
                    claimPoolInfo.claimCode = 1;
                    claimPoolInfo.claimPool = 0;
                    claimPoolInfo.pCount = 0;
                    claimPoolInfo.saleCount = 0;
                    claimPoolInfo.shareCount = 0;
                    claimPoolInfo.teamCount = 0;
                    claimPoolInfo.commCount = 0;
                    claimPoolInfo.claimState = 0;
                    var key = "claim_".AsByteArray().Concat(claimPoolInfo.claimCode.AsByteArray());
                    Storage.Put(Storage.CurrentContext, key, Helper.Serialize(claimPoolInfo));
                    bol = true;
                }
            }
            //
            if (bol == true)
            {
                Deployed(1);
            }else{
                Deployed(0);
            }
            return bol;
        }

        /**
         * 打开合约
         */ 
        public static bool openBPEC()
        {
            BPECPoolInfo bpecPoolInfo = getBPECPoolInfo();
            bpecPoolInfo.bpecState = 1;
            //保存股权余额
            Storage.Put(Storage.CurrentContext, "BPECPoolInfo", Helper.Serialize(bpecPoolInfo));
            OpenBPECed(1);
            return true;
        }

        /**
         * 关闭合约
         */ 
        public static bool closeBPEC()
        {
            BPECPoolInfo bpecPoolInfo = getBPECPoolInfo();
            bpecPoolInfo.bpecState = 0;
            //保存股权余额
            Storage.Put(Storage.CurrentContext, "BPECPoolInfo", Helper.Serialize(bpecPoolInfo));
            CloseBPECed(1);
            return true;
        }

        /**
         * 查询合约状态
         */
        public static byte getBPECState()
        {
            BPECPoolInfo bpecPoolInfo = getBPECPoolInfo();
            return bpecPoolInfo.bpecState;
        }

        /**
         * 查询合约初始信息
         * 
         */ 
        public static BPECInfo getBPECInfo()
        {
            byte[] data = Storage.Get(Storage.CurrentContext, "BPECInfo");
            if (data.Length > 0)
            {
               return Helper.Deserialize(data) as BPECInfo;
            }
            else
            {
                return new BPECInfo();
            }
        }

        /**
         * 分红池信息查询
         **/
        public static ClaimPoolInfo getClaimPoolInfo(BigInteger claimId)
        {
            var key = "claim_".AsByteArray().Concat(claimId.AsByteArray());
            byte[] data = Storage.Get(Storage.CurrentContext, key);
            if (data.Length > 0)
            {
                return Helper.Deserialize(data) as ClaimPoolInfo;
            }
            else
            {
                return new ClaimPoolInfo(); ;
            }
        }

        /**
         * 当前分红池信息查询
         **/
        public static object[] getNowClaimPoolInfo()
        {
            BPECPoolInfo BPECPoolInfo = getBPECPoolInfo();
            var key = "claim_".AsByteArray().Concat(BPECPoolInfo.claimCode.AsByteArray());
            byte[] data = Storage.Get(Storage.CurrentContext, key);
            if (data.Length > 0)
            {
                return (object[])Helper.Deserialize(data);
            }
            else
            {
                return new object[0];
            }
        }

        /**
         * 查询合约股权余额信息
         * 
         **/
        public static BPECPoolInfo getBPECPoolInfo()
        {
            byte[] data = Storage.Get(Storage.CurrentContext, "BPECPoolInfo");
            if (data.Length == 0)
            {
                return new BPECPoolInfo();
            }
            else
            {
                return Helper.Deserialize(data) as BPECPoolInfo;
                
            }
        }

        /**
         * 查询合约交易记录
         * 
         **/
        public static TransferInfo getTransferInfo(byte[] txid)
        {
            var key = "log_".AsByteArray().Concat(txid);
            byte[] data = Storage.Get(Storage.CurrentContext, key);
            TransferInfo transferInfo = null;
            if (data.Length > 0)
            {
                transferInfo = Helper.Deserialize(data) as TransferInfo;
            }
            return transferInfo;
        }

        /**
         * 用户交易股权
         * 
         **/
        public static bool transfer(byte[] from, byte[] to, BigInteger value)
        {
            bool bol = false;
            BigInteger claimId = 0;
            if (from.Length > 0&& to.Length > 0&&value > 0 && from != to)
            {
                BPECPoolInfo bpecPoolInfo = getBPECPoolInfo();
                if (bpecPoolInfo.bpecState == 1)
                {
                    claimId = bpecPoolInfo.claimCode;
                    //
                    BPECInfo bpecInfo = getBPECInfo();
                    //获得当前块的高度
                    BigInteger height = Blockchain.GetHeight();
                    //付款方
                    var keyFrom = new byte[] { 0x11 }.Concat(from);
                    Info fromInfo = getInfo(from);
                    var from_value = fromInfo.balance;
                    if (from_value >= value)
                    {
                        fromInfo.block = height;
                        fromInfo.balance = from_value - value;
                        Storage.Put(Storage.CurrentContext, keyFrom, Helper.Serialize(fromInfo));
                        //收款方
                        var keyTo = new byte[] { 0x11 }.Concat(to);
                        Info toInfo = getInfo(to);
                        var to_value = toInfo.balance;
                        toInfo.block = height;
                        toInfo.balance = to_value + value;
                        Storage.Put(Storage.CurrentContext, keyTo, Helper.Serialize(toInfo));
                        //保存交易记录
                        /*TransferInfo trInfo = new TransferInfo();
                        trInfo.from = from;
                        trInfo.to = to;
                        trInfo.ttp = 1;
                        trInfo.value = value;
                        txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
                        Storage.Put(Storage.CurrentContext, txid, Helper.Serialize(trInfo));*/
                        bol = true;
                    }
                }
                //notify
                if (bol==true) {
                    Transfered(1,from, to,1, value, claimId);
                }
                else
                {
                    Transfered(0, from, to,1, value, claimId);
                }
            }
            return bol;
        }

        /**
         * 购买、分享、解禁交易股权
         * 
         **/
        public static bool transferOpen(byte[] owner, byte ttp, BigInteger value,BigInteger cValue)
        {
            bool bol = false;
            BigInteger gold = value;
            if (owner.Length > 0 && ttp > 0 && value > 0 && cValue>0)
            {
                //获得当前块的高度
                BigInteger height = Blockchain.GetHeight();
                //
                ClaimPoolInfo claimPoolInfo = null;
                BPECPoolInfo bpecPoolInfo = getBPECPoolInfo();
                bool isEnd = false;
                if (bpecPoolInfo.bpecState==1) {
                    claimPoolInfo = getClaimPoolInfo(bpecPoolInfo.claimCode);
                    //
                    if (ttp == 2)
                    {
                        if (bpecPoolInfo.obpecBalance > 0)
                        {
                            if (bpecPoolInfo.obpecBalance > value)
                            {
                                bpecPoolInfo.obpecBalance -= value;
                            }
                            else
                            {
                                value = bpecPoolInfo.obpecBalance;
                                bpecPoolInfo.obpecBalance = 0;
                                bpecPoolInfo.endBuyAddress = owner;
                                isEnd = true;
                                if (bpecPoolInfo.obpecOver == 1)
                                {
                                    bpecPoolInfo.obpecOver = 0;
                                }
                            }
                            //
                            claimPoolInfo.buyAddress = owner;
                            claimPoolInfo.saleCount += value;
                            claimPoolInfo.claimPool += cValue;
                        }
                        else
                        {
                            //如果股权已经分完，则只累计分红池CAGS，不分发股权
                            claimPoolInfo.buyAddress = owner;
                           
                            claimPoolInfo.claimPool += cValue;
                            
                        }
                        bol = true;
                    }
                    if (ttp == 3)
                    {
                        if (bpecPoolInfo.obpecBalance > 0)
                        {
                            if (bpecPoolInfo.obpecBalance > value)
                            {
                                bpecPoolInfo.obpecBalance -= value;
                            }
                            else
                            {
                                value = bpecPoolInfo.obpecBalance;
                                bpecPoolInfo.obpecBalance = 0;
                                bpecPoolInfo.endBuyAddress = claimPoolInfo.buyAddress;
                                isEnd = true;
                                if (bpecPoolInfo.obpecOver == 1)
                                {
                                    bpecPoolInfo.obpecOver = 0;
                                }
                            }
                            claimPoolInfo.shareCount += value;
                            bol = true;
                        }
                    }
                    else if (ttp == 4)
                    {
                        if (bpecPoolInfo.cbpecBalance > 0 && bpecPoolInfo.cbpecBalance >= value)
                        {
                            bpecPoolInfo.cbpecBalance -= value;
                            claimPoolInfo.commCount += value;
                            bol = true;
                        }
                    }
                    else if (ttp == 5)
                    {
                        if (bpecPoolInfo.tbpecBalance > 0 && bpecPoolInfo.tbpecBalance >= value)
                        {
                            bpecPoolInfo.tbpecBalance -= value;
                            claimPoolInfo.teamCount += value;
                            bol = true;
                        }
                    }
                }
                //
                if (bol==true)
                {
                    //保存分红池
                    var key = "claim_".AsByteArray().Concat(claimPoolInfo.claimCode.AsByteArray());
                    Storage.Put(Storage.CurrentContext, key, Helper.Serialize(claimPoolInfo));
                    if (ttp == 2 || ttp == 3)
                    {
                        if (bpecPoolInfo.obpecOver == 1 || (bpecPoolInfo.obpecOver == 0 && isEnd == true))
                        {
                            //保存股权余额
                            bpecPoolInfo.totalBPEC += value;
                            Storage.Put(Storage.CurrentContext, "BPECPoolInfo", Helper.Serialize(bpecPoolInfo));
                            //保存用户股权余额信息
                            Info ownerInfo = getInfo(owner);
                            ownerInfo.balance += value;
                            ownerInfo.block = height;
                            var keyOwner = new byte[] { 0x11 }.Concat(owner);
                            Storage.Put(Storage.CurrentContext, keyOwner, Helper.Serialize(ownerInfo));
                        }
                    }
                    else
                    {
                        //保存股权余额
                        bpecPoolInfo.totalBPEC += value;
                        Storage.Put(Storage.CurrentContext, "BPECPoolInfo", Helper.Serialize(bpecPoolInfo));
                        //保存用户股权余额信息
                        Info ownerInfo = getInfo(owner);
                        ownerInfo.balance += value;
                        ownerInfo.block = height;
                        var keyOwner = new byte[] { 0x11 }.Concat(owner);
                        Storage.Put(Storage.CurrentContext, keyOwner, Helper.Serialize(ownerInfo));
                    }
                    //保存交易记录
                    /*TransferInfo trInfo = new TransferInfo();
                    trInfo.from = owner;
                    trInfo.to = owner;
                    trInfo.ttp = ttp;
                    trInfo.value = value;
                    txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
                    Storage.Put(Storage.CurrentContext, txid, Helper.Serialize(trInfo));*/
                }
                //notify
                if (bol == true)
                {
                    TransferOpened(1, owner, ttp, gold, value, bpecPoolInfo.obpecOver, bpecPoolInfo.claimCode);
                }
                else
                {
                    TransferOpened(0, owner, ttp, gold, value, bpecPoolInfo.obpecOver, bpecPoolInfo.claimCode);
                }
            }
            return bol;
        }

        /**
         * 保存交易记录
         * 
         */ 
        public static bool updateTransferInfo(byte[] from, byte[] to, byte ttp, BigInteger value,byte[] txid)
        {
            TransferInfo trInfo = new TransferInfo();
            trInfo.from = from;
            trInfo.to = to;
            trInfo.ttp = ttp;
            trInfo.value = value;
            var key = "log_".AsByteArray().Concat(txid);
            Storage.Put(Storage.CurrentContext, txid, Helper.Serialize(trInfo));
            return true;
        }

        /**
         * 保存合法合约
         * 
         */
        public static bool saveContractHash(byte[] cHash)
        {
            if (Runtime.CheckWitness(ContractOwner))
            {
                Storage.Put(Storage.CurrentContext, cHash, cHash);
                return true;
            }
            return false;
        }

        /**
        * 开启分红
        * 
        */
        public static bool satrtClaim()
        {
            bool bol = false;
            if (Runtime.CheckWitness(ContractOwner))
            {
                BPECPoolInfo bpecPoolInfo = getBPECPoolInfo();
                if (bpecPoolInfo.bpecState == 1)
                {
                    ClaimPoolInfo claimPoolInfo = getClaimPoolInfo(bpecPoolInfo.claimCode);
                    if (claimPoolInfo.claimState==0)
                    {
                        //
                        ClaimPoolInfo claimPoolInfoTemp = new ClaimPoolInfo();
                        claimPoolInfoTemp.claimCode = claimPoolInfo.claimCode+1;
                        claimPoolInfoTemp.claimPool = 0;
                        claimPoolInfoTemp.pCount = 0;
                        claimPoolInfoTemp.saleCount = 0;
                        claimPoolInfoTemp.shareCount = 0;
                        claimPoolInfoTemp.teamCount = 0;
                        claimPoolInfoTemp.commCount = 0;
                        claimPoolInfoTemp.buyAddress = claimPoolInfo.buyAddress;
                        claimPoolInfoTemp.claimState = 0;
                        //
                        var key = "claim_".AsByteArray().Concat(claimPoolInfoTemp.claimCode.AsByteArray());
                        Storage.Put(Storage.CurrentContext, key, Helper.Serialize(claimPoolInfoTemp));
                        //
                        bpecPoolInfo.claimCode = claimPoolInfoTemp.claimCode;
                        Storage.Put(Storage.CurrentContext, "BPECPoolInfo", Helper.Serialize(bpecPoolInfo));
                        //
                        claimPoolInfo.claimState = 1;
                        var key1 = "claim_".AsByteArray().Concat(claimPoolInfo.claimCode.AsByteArray());
                        Storage.Put(Storage.CurrentContext, key1, Helper.Serialize(claimPoolInfo));
                        bol = true;
                    }
                    //notify
                    if (bol==true)
                    {
                        StartClaimed(1, claimPoolInfo.claimCode, claimPoolInfo.claimPool, bpecPoolInfo.totalBPEC, claimPoolInfo.claimCode+1);
                    }
                    else
                    {
                        StartClaimed(0, claimPoolInfo.claimCode, claimPoolInfo.claimPool, bpecPoolInfo.totalBPEC, claimPoolInfo.claimCode + 1);
                    }

                }
            }

            return bol;
        }

        /**
         * 用户股权余额查询
         * 
         */
        public static BigInteger balanceOf(byte[] who)
        {
            var addressInfo = getInfo(who);
            return addressInfo.balance;
        }

        /**
         * 用户股权信息查询
         **/ 
        public static Info getInfo(byte[] who)
        {
            var keyWho = new byte[] { 0x11 }.Concat(who);
            byte[] data = Storage.Get(Storage.CurrentContext, keyWho);
            Info info = new Info();
            if (data.Length > 0)
            {
                info = Helper.Deserialize(data) as Info;
            }
            else
            {
                info.balance = 0;
                info.block = 0;
            }
            return info;
        }



        public static object Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return true;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //必须在入口函数取得callscript，调用脚本的函数，也会导致执行栈变化，再取callscript就晚了
                var callscript = ExecutionEngine.CallingScriptHash;
                //判断合约是否开启


                //this is in nep5
                if (method == "totalSupply")
                {
                    if (args.Length != 1) return 0;
                    byte type = (byte)args[0];
                    return totalSupply(type);
                }
                if (method == "name") return name();
                if (method == "version") return Version();
                if (method == "symbol") return symbol();
                if (method == "decimals") return decimals();
                if (method == "deploy")
                {
                    return deploy();
                }
                if (method == "openBPEC")
                {
                    return openBPEC();
                }
                if (method == "closeBPEC")
                {
                    return closeBPEC();
                }
                if (method == "getBPECInfo")
                {
                    return getBPECInfo();
                }
                
                if (method == "getBPECPoolInfo")
                {
                    return getBPECPoolInfo();
                }
                if (method == "getTransferInfo")
                {
                    if (args.Length != 1) return false;
                    byte[] txid = (byte[])args[0];
                    return getTransferInfo(txid);
                }
                if (method == "getBPECState")
                {
                    return getBPECState();
                }
                if (method == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] who = (byte[])args[0];
                    return balanceOf(who);
                }
                if (method == "getInfo")
                {
                    if (args.Length != 1) return 0;
                    byte[] who = (byte[])args[0];
                    return getInfo(who);
                }
                if (method == "getClaimPoolInfo")
                {
                    if (args.Length != 1) return 0;
                    BigInteger claimId = (BigInteger)args[0];
                    return getClaimPoolInfo(claimId);
                }
                if (method == "getNowClaimPoolInfo")
                {
                    return getNowClaimPoolInfo();
                }
                if (method == "getContractHash")
                {
                    if (args.Length != 1) return false;
                    byte[] cHash = (byte[])args[0];
                    return Storage.Get(Storage.CurrentContext, cHash);
                }
                if (method == "saveContractHash")
                {
                    if (args.Length != 1) return false;
                    byte[] cHash = (byte[])args[0];
                    return saveContractHash(cHash);
                }
                if (method == "satrtClaim")
                {
                    if (args.Length != 1) return false;
                    return satrtClaim();
                }
                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    if (from == to)
                        return true;
                    if (from.Length == 0 || to.Length == 0)
                        return false;
                    BigInteger value = (BigInteger)args[2] / 1000000;
                    //没有from签名，不让转
                    if (!Runtime.CheckWitness(from))
                        return false;
                    //
                    byte[] callSave = Storage.Get(Storage.CurrentContext, callscript);
                    if (callSave.AsBigInteger() != callscript.AsBigInteger())
                        return false;

                    return transfer(from, to, value);
                }
                if (method == "transferOpen")
                {
                    if (args.Length != 4) return false;
                    byte[] from = (byte[])args[0];
                    byte ttp = (byte)args[1];
                    if (from.Length == 0 || ttp == 0)
                        return false;
                    BigInteger value = 0;
                    BigInteger cValue = 0;
                    if (ttp == 2)
                    {
                        //没有from签名，不让转
                        if (!Runtime.CheckWitness(from))
                            return false;
                        //
                        value = (BigInteger)args[2] / 1000000;
                        cValue = (BigInteger)args[3];
                        //
                        byte[] callSave = Storage.Get(Storage.CurrentContext, callscript);
                        if (callSave.AsBigInteger() != callscript.AsBigInteger())
                            return false;
                    }
                    else if(ttp==3|| ttp == 4|| ttp == 5)
                    { 
                        //如果ttp>2,则需要校验是否合约拥有者
                        if (!Runtime.CheckWitness(ContractOwner))
                        {
                            return false;
                        }
                        value = (BigInteger)args[2];
                        cValue = (BigInteger)args[3];
                    }
                    else
                    {
                        return false;
                    }
                    return transferOpen(from, ttp, value, cValue);
                }
                if (method == "updateTransferInfo")
                {
                    if (args.Length != 5) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    if (from == to)
                        return true;
                    if (from.Length == 0 || to.Length == 0)
                        return false;
                    byte ttp = (byte)args[2];
                    BigInteger value = (BigInteger)args[3];
                    //没有from签名，不让转
                    if (!Runtime.CheckWitness(ContractOwner))
                        return false;
                    byte[] txid = (byte[])args[4];
   

                    return updateTransferInfo(from, to, ttp, value,txid);
                }
                #region 升级合约,耗费490,仅限管理员
                if (method == "upgrade")
                {
                    //不是管理员 不能操作
                    if (!Runtime.CheckWitness(ContractOwner))
                        return false;

                    if (args.Length != 1 && args.Length != 9)
                        return false;

                    byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
                    byte[] new_script = (byte[])args[0];
                    //如果传入的脚本一样 不继续操作
                    if (script == new_script)
                        return false;

                    byte[] parameter_list = new byte[] { 0x07, 0x10 };
                    byte return_type = 0x05;
                    bool need_storage = (bool)(object)05;
                    string name = "BPEC";
                    string version = "1";
                    string author = "CG";
                    string email = "0";
                    string description = "BPEC";

                    if (args.Length == 9)
                    {
                        parameter_list = (byte[])args[1];
                        return_type = (byte)args[2];
                        need_storage = (bool)args[3];
                        name = (string)args[4];
                        version = (string)args[5];
                        author = (string)args[6];
                        email = (string)args[7];
                        description = (string)args[8];
                    }
                    Contract.Migrate(new_script, parameter_list, return_type, need_storage, name, version, author, email, description);
                    return true;
                }
                #endregion
            }

            return false;
        } 
    }
}