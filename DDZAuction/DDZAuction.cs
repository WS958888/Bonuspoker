using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;

namespace DDZAuction
{
    public class DDZAuction : SmartContract
    {

        // CGAS合约hash
        delegate object deleDyncall(string method, object[] arr);

        // BPEC合约hash
        [Appcall("fa45ea4e8034028243db9d35210ccd3db5a4b178")]
        static extern object bpecCall(string method, object[] arr);

        // the owner, super admin address  
        public static readonly byte[] ContractOwner = "AUGkNMWzBCy5oi1rFKR5sPhpRjhhfgPhU2".ToScriptHash();
        //拍卖行分红账号
        public static readonly byte[] AuctionOwner = "AUGkNMWzBCy5oi1rFKR5sPhpRjhhfgPhU2".ToScriptHash();
        //基础块数
        private const ulong BASE_BLOCK = 500;
        //增加的块数
        private const ulong ADD_BLOCK = 20;
        //最大块数
        private const ulong MAX_BLOCK = 2000;
        //CGAS和CLAIM换算比例100就是1
        private const ulong CGAS_CLAIM_RATE = 3;
        //分红比例
        private const ulong CLAIM_RATE = 75;
        //奖池比例
        private const ulong COIN_RATE = 5;
        //手续费用比例
        private const ulong FEE_RATE = 20;

        // 拍卖成交记录
        public class AuctionRecord
        {
            public BigInteger tokenId;
            public byte[] seller;
            public byte[] buyer;
            public BigInteger sellPrice;
            public BigInteger value;
            public BigInteger sellTime;
            public byte recordState;//0-上架;1-成交
        }
        /**
        *奖池数据结构
        */
        public class CoinPoolInfo
        {
            public BigInteger coinCode;//奖池序号
            public BigInteger lastBlock;//上一次开奖区块
            public BigInteger nowBlock;//当前区块
            public BigInteger nextBlock;//下一次开奖区块
            public byte[] winAddress;//最后获奖地址
            public BigInteger coinPool;//奖池余额
            public byte claimState;//奖池状态:0-未发;1-已发
            public byte claimEnd;//是否结束分红:0-未结束;1-结束
        }

        /**
        *用户数据结构
        */
        public class UserInfo
        {
            public BigInteger balance = 0;//CGAS余额
            public BigInteger gold = 0;//游戏币余额
        }

        // notify 购买解禁BPEC
        public delegate void deleTransferBPEC(byte state, byte[] sender, BigInteger value,BigInteger coinId);
        [DisplayName("transferBPEC")]
        public static event deleTransferBPEC TransferBPECed;

        // notify 拍卖出售BPEC
        public delegate void deleSaleBPEC(byte state, byte[] owner, BigInteger value, BigInteger tokenId, uint sellTime);
        [DisplayName("saleBPEC")]
        public static event deleSaleBPEC SaleBPECed;

        //notify 取消拍卖通知
        public delegate void deleCancelBPEC(byte state, byte[] owner, BigInteger tokenId);
        [DisplayName("cancelBPEC")]
        public static event deleCancelBPEC CancelBPECed;

        // notify 购买拍卖BPEC
        public delegate void deleBuyBPEC(byte state, byte[] owner, BigInteger tokenId);
        [DisplayName("buyBPEC")]
        public static event deleBuyBPEC BuyBPECed;

        // notify 玩家领取分红
        public delegate void deleRecClaim(byte state, byte[] owner, BigInteger value);
        [DisplayName("recClaim")]
        public static event deleRecClaim RecClaimed;

        // notify 玩家领取分红
        public delegate void deleTransferABC(byte state, byte[] owner,byte[] to,BigInteger tokenId,BigInteger pabc, BigInteger value);
        [DisplayName("transferABC")]
        public static event deleTransferABC TransferABCed;
        
        /**
         * 名称
         */
        public static string name()
        {
            return "DDZAuction";
        }
        public static string symbol()
        {
            return "DDZ";
        }
        /**
         * 精度
         */
        public static byte decimals()
        {
            return 8;
        }

        /**
         * 版本
         */
        public static string Version()
        {
            return "1.1.1";
        }

        /**
         * 存储增加的代币数量
         */
        private static void _addTotal(BigInteger count)
        {
            BigInteger total = Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();
            total += count;
            Storage.Put(Storage.CurrentContext, "totalExchargeSgas", total);
        }
        /**
         * 不包含收取的手续费在内，所有用户存在拍卖行中的代币
         */
        public static BigInteger totalExchargeSgas()
        {
            return Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();
        }

        /**
         * 存储减少的代币数总量
         */
        private static void _subTotal(BigInteger count)
        {
            BigInteger total = Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();
            total -= count;
            if (total > 0)
            {
                Storage.Put(Storage.CurrentContext, "totalExchargeSgas", total);
            }
            else
            {

                Storage.Delete(Storage.CurrentContext, "totalExchargeSgas");
            }
        }

        /**
         * 用户在拍卖所存储的代币
         */
        public static BigInteger balanceOf(byte[] address)
        {
            UserInfo userInfo = getUserInfo(address);
            return userInfo.balance;
        }

        /**
         * 用户游戏币查询
         */
        public static BigInteger balanceOfGold(byte[] address)
        {
            UserInfo userInfo = getUserInfo(address);
            return userInfo.gold;
        }

        /**
         * 奖池信息查询
         **/
        public static CoinPoolInfo getCoinPoolInfo(BigInteger coinId)
        {
            byte[] key = "coin_".AsByteArray().Concat(coinId.AsByteArray());
            byte[] data = Storage.Get(Storage.CurrentContext, key);
            CoinPoolInfo info = null;
            if (data.Length > 0)
            {
                info = Helper.Deserialize(data) as CoinPoolInfo;
            }
            else
            {
                info = new CoinPoolInfo();
            }
            info.nowBlock = Blockchain.GetHeight();
            return info;
        }

        /**
         * 用户股权信息查询
         **/
        public static UserInfo getUserInfo(byte[] who)
        {
            var keyWho = new byte[] { 0x11 }.Concat(who);
            byte[] data = Storage.Get(Storage.CurrentContext, keyWho);
            UserInfo info = null;
            if (data.Length > 0)
            {
                info = Helper.Deserialize(data) as UserInfo;
            }
            else
            {
                info = new UserInfo();
            }
            return info;
        }

        /**
         * 游戏币交换
         */ 
        public static bool transferABC(byte[] from, byte[] to, BigInteger tokenId, BigInteger pabc, BigInteger value)
        {
            bool bol = false;
            if (from.Length == 20 && to.Length == 20 && pabc > 0 && value > 0) { 
                UserInfo fromUser = getUserInfo(from);
                UserInfo toUser = getUserInfo(to);
                if (fromUser.balance>=pabc&&toUser.gold>= value)
                {
                    fromUser.balance -= pabc;
                    fromUser.gold += value;
                    toUser.balance += pabc;
                    toUser.gold -= value;
                    byte[] keyfrom = new byte[] { 0x11 }.Concat(from);
                    byte[] keyto = new byte[] { 0x11 }.Concat(to);
                    Storage.Put(Storage.CurrentContext, keyfrom, Helper.Serialize(fromUser));
                    Storage.Put(Storage.CurrentContext, keyto, Helper.Serialize(toUser));
                    bol = true;
                }
                if (bol==true)
                {
                    TransferABCed(1,from,to,tokenId,pabc,value);
                }
                else
                {
                    TransferABCed(0, from, to, tokenId, pabc, value);
                }
            }
            return bol;
        }

        /**
         * 该txid是否已经充值过
         */
        public static bool hasAlreadyCharged(byte[] txid)
        {
            //2018/6/5 cwt 修补漏洞
            byte[] keytxid = new byte[] { 0x11 }.Concat(txid);
            byte[] txinfo = Storage.Get(Storage.CurrentContext, keytxid);
            if (txinfo.Length > 0)
            {
                // 已经处理过了
                return false;
            }
            return true;
        }


        /**
         * 提币
         */
        public static bool drawToken(byte[] sender, BigInteger count)
        {
            if (sender.Length != 20)
            {
                Runtime.Log("Owner error.");
                return false;
            }

            //2018/6/5 cwt 修补漏洞
            byte[] keytsender = new byte[] { 0x11 }.Concat(sender);

            if (Runtime.CheckWitness(sender))
            {
                BigInteger nMoney = 0;
                //byte[] ownerMoney = Storage.Get(Storage.CurrentContext, keytsender);
                UserInfo userInfo = getUserInfo(sender);
                nMoney = userInfo.balance;
                if (count <= 0 || count > nMoney)
                {
                    // 全部提走
                    count = nMoney;
                }

                // 转账
                object[] args = new object[4] { ExecutionEngine.ExecutingScriptHash, sender, count, ExecutionEngine.ExecutingScriptHash };
                byte[] sgasHash = Storage.Get(Storage.CurrentContext, "sgas");
                deleDyncall dyncall = (deleDyncall)sgasHash.ToDelegate();
                bool res = (bool)dyncall("transfer", args);
                if (!res)
                {
                    return false;
                }

                // 记账
                nMoney -= count;

                _subTotal(count);

                if (nMoney > 0)
                {
                    userInfo.balance = nMoney;
                    Storage.Put(Storage.CurrentContext, keytsender, Helper.Serialize(userInfo));
                }
                else
                {
                    Storage.Delete(Storage.CurrentContext, keytsender);
                }

                return true;
            }
            return false;
        }

        /**
         * 将收入提款到合约拥有者
         */
        public static bool drawToContractOwner(BigInteger flag, BigInteger count)
        {
            if (Runtime.CheckWitness(ContractOwner))
            {
                BigInteger nMoney = 0;
                // 查询余额
                object[] args = new object[1] { ExecutionEngine.ExecutingScriptHash };
                byte[] sgasHash = Storage.Get(Storage.CurrentContext, "sgas");
                deleDyncall dyncall = (deleDyncall)sgasHash.ToDelegate();
                BigInteger totalMoney = (BigInteger)dyncall("balanceOf", args);
                BigInteger supplyMoney = Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();
                if (flag == 0)
                {
                    BigInteger canDrawMax = totalMoney - supplyMoney;
                    if (count <= 0 || count > canDrawMax)
                    {
                        // 全部提走
                        count = canDrawMax;
                    }
                }
                else
                {
                    //由于官方SGAS合约实在太慢，为了保证项目上线，先发行自己的SGAS合约方案，预留出来迁移至官方sgas用的。
                    count = totalMoney;
                    nMoney = 0;
                    Storage.Put(Storage.CurrentContext, "totalExchargeSgas", nMoney);
                }
                // 转账
                args = new object[4] { ExecutionEngine.ExecutingScriptHash, ContractOwner, count, ExecutionEngine.ExecutingScriptHash };

                deleDyncall dyncall2 = (deleDyncall)sgasHash.ToDelegate();
                bool res = (bool)dyncall2("transfer", args);
                if (!res)
                {
                    return false;
                }

                // 记账  cwt此处不应该记账
                //_subTotal(count);
                return true;
            }
            return false;
        }

        public static BigInteger getAuctionAllFee()
        {
            BigInteger nMoney = 0;
            // 查询余额
            object[] args = new object[1] { ExecutionEngine.ExecutingScriptHash };
            byte[] sgasHash = Storage.Get(Storage.CurrentContext, "sgas");
            deleDyncall dyncall = (deleDyncall)sgasHash.ToDelegate();
            BigInteger totalMoney = (BigInteger)dyncall("balanceOf", args);
            BigInteger supplyMoney = Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();

            BigInteger canDrawMax = totalMoney - supplyMoney;
            return canDrawMax;
        }

        /**
         * 使用txid充值
         */
        public static bool rechargeToken(byte[] owner, byte[] txid)
        {
            if (owner.Length != 20)
            {
                Runtime.Log("Owner error.");
                return false;
            }

            //2018/6/5 cwt 修补漏洞
            byte[] keytxid = new byte[] { 0x11 }.Concat(txid);
            byte[] keytowner = new byte[] { 0x11 }.Concat(owner);

            byte[] txinfo = Storage.Get(Storage.CurrentContext, keytxid);
            if (txinfo.Length > 0)
            {
                // 已经处理过了
                return false;
            }


            // 查询交易记录
            object[] args = new object[1] { txid };
            byte[] sgasHash = Storage.Get(Storage.CurrentContext, "sgas");
            deleDyncall dyncall = (deleDyncall)sgasHash.ToDelegate();
            object[] res = (object[])dyncall("getTxInfo", args);

            if (res.Length > 0)
            {
                byte[] from = (byte[])res[0];
                byte[] to = (byte[])res[1];
                BigInteger value = (BigInteger)res[2];

                if (from == owner)
                {
                    if (to == ExecutionEngine.ExecutingScriptHash)
                    {
                        // 标记为处理
                        Storage.Put(Storage.CurrentContext, keytxid, value);

                        BigInteger nMoney = 0;
                        UserInfo userInfo = getUserInfo(owner);

                        nMoney = userInfo.balance;
                        nMoney += value;

                        _addTotal(value);
                        userInfo.balance = nMoney;
                        // 记账
                        Storage.Put(Storage.CurrentContext, keytowner, Helper.Serialize(userInfo));
                        return true;
                    }
                }
            }
            return false;
        }

        /**
         * 购买解禁BPEC
         */ 
        public static bool transferBPEC(byte[] sender, BigInteger value)
        {
            bool bol = false;
            if (sender.Length != 20|| value<=0)
            {
                Runtime.Log("Owner error.");
                return false;
            }
            if (Runtime.CheckWitness(sender))
            {
                var height = Blockchain.GetHeight();
                CoinPoolInfo coinPoolInfo = getCoinPoolInfo(0);
                if (coinPoolInfo.coinCode == 0)
                {
                    coinPoolInfo.coinCode = 1;
                    coinPoolInfo.lastBlock = height;
                    coinPoolInfo.nowBlock = height;
                    coinPoolInfo.nextBlock = height + BASE_BLOCK;
                    coinPoolInfo.coinPool = 0;
                    coinPoolInfo.claimState = 0;
                    coinPoolInfo.claimEnd = 0;
                }
                //
                if (height > coinPoolInfo.nextBlock)
                {
                    if (coinPoolInfo.claimState == 0) { 
                        object[] args = new object[0] {};
                        object[] res = (object[])bpecCall("getBPECPoolInfo", args);
                        if (res.Length > 0)
                        {
                            BigInteger claimCode = (BigInteger)res[4];
                            byte[] endAddress = (byte[])res[5];
                            byte obpecOver = (byte)res[6];
                            BigInteger coin = 0;
                            object[] args2 = new object[1] { claimCode };
                            object[] res2 = (object[])bpecCall("getClaimPoolInfo", args2);
                            if (res2.Length > 0)
                            {
                                byte[] endBuyAddress = (byte[])res2[7];
                                if (coinPoolInfo.claimEnd == 0)
                                {
                                    if (obpecOver == 0)
                                    {
                                        coin = coinPoolInfo.coinPool;
                                        coinPoolInfo.claimEnd = 1;
                                        coinPoolInfo.winAddress = endAddress;
                                    }
                                    else
                                    {
                                        coin = coinPoolInfo.coinPool / 2;
                                        coinPoolInfo.winAddress = endBuyAddress;
                                    }
                                }
                                else
                                {
                                    coin = coinPoolInfo.coinPool / 2;
                                    coinPoolInfo.winAddress = endBuyAddress;
                                }
                            
                                //创建保存最新的奖池
                                CoinPoolInfo coinPoolInfoTemp = new CoinPoolInfo();
                                coinPoolInfoTemp.coinCode = coinPoolInfo.coinCode + 1;
                                coinPoolInfoTemp.lastBlock = height;
                                coinPoolInfo.nowBlock = height;
                                coinPoolInfoTemp.nextBlock = height + BASE_BLOCK;
                                coinPoolInfoTemp.coinPool = coinPoolInfo.coinPool - coin;
                                coinPoolInfoTemp.claimState = 0;
                                coinPoolInfoTemp.claimEnd = coinPoolInfo.claimEnd;
                                coinPoolInfoTemp.winAddress = endBuyAddress;
                                BigInteger n = 0;
                                var key = "coin_".AsByteArray().Concat(n.AsByteArray());
                                Storage.Put(Storage.CurrentContext, key, Helper.Serialize(coinPoolInfoTemp));
                                //分离奖池并开奖
                                coinPoolInfo.claimState = 1;
                                coinPoolInfo.coinPool = coinPoolInfoTemp.coinPool;
                                byte[] key2 = "coin_".AsByteArray().Concat(coinPoolInfo.coinCode.AsByteArray());
                                Storage.Put(Storage.CurrentContext, key2, Helper.Serialize(coinPoolInfo));
                                //发放奖励
                                UserInfo userInfo = getUserInfo(coinPoolInfo.winAddress);
                                userInfo.balance += coin;
                                var keyWho = new byte[] { 0x11 }.Concat(coinPoolInfo.winAddress);
                                Storage.Put(Storage.CurrentContext, keyWho, Helper.Serialize(userInfo));
                                //notify
                                TransferBPECed(2, sender, coinPoolInfo.coinPool, coinPoolInfo.coinCode);
                                bol = true;
                            }
                        }
                    }
                }
                else {
                    BigInteger cgas = value * CGAS_CLAIM_RATE/100;
                    BigInteger claimV = cgas * CLAIM_RATE / 100;
                    BigInteger coinV = cgas * COIN_RATE / 100;
                    BigInteger feeV = cgas - claimV - coinV;
                    UserInfo userInfo = getUserInfo(sender);
                    if (userInfo.balance >= cgas) {
                        object[] args = new object[4] { sender, 2, value, claimV };
                        bool res = (bool)bpecCall("transferOpen", args);
                        if (res)
                        {
                            //
                            _subTotal(feeV);
                            //
                            coinPoolInfo.coinPool += coinV;
                            coinPoolInfo.nextBlock += ADD_BLOCK;
                            coinPoolInfo.nowBlock = height;
                            //判断最大值
                            if (height > coinPoolInfo.nextBlock)
                            {
                                coinPoolInfo.nextBlock = coinPoolInfo.lastBlock + MAX_BLOCK;
                            }
                            BigInteger n = 0;
                            var key = "coin_".AsByteArray().Concat(n.AsByteArray());
                            Storage.Put(Storage.CurrentContext, key, Helper.Serialize(coinPoolInfo));
                            //
                            userInfo.balance -= cgas;
                            var keyWho = new byte[] { 0x11 }.Concat(sender);
                            Storage.Put(Storage.CurrentContext, keyWho, Helper.Serialize(userInfo));
                            bol = true;
                        }
                    }
                    if (bol==true)
                    {
                        TransferBPECed(1, sender,  value, coinPoolInfo.coinCode);
                    }
                    else
                    {
                        TransferBPECed(0, sender,  value, coinPoolInfo.coinCode);
                    }
                }
            }
            return bol;
        }

        /**
         * 拍卖出售BPEC
         */
        public static bool saleBPEC(byte[] owner, BigInteger price,BigInteger value)
        {
            bool bol = false;
            var recordKey = Storage.Get(Storage.CurrentContext, "AuctionRecord").AsBigInteger();
            if (Runtime.CheckWitness(owner)&&owner.Length == 20 && value > 0)
            {
                object[] args = new object[3] { owner, AuctionOwner, value };
                bool res = (bool)bpecCall("transfer", args);
                if (res)
                {
                    //
                    var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
                    AuctionRecord record = new AuctionRecord();
                    record.tokenId += 1;
                    record.seller = owner;
                    record.sellTime = nowtime;
                    record.sellPrice = price;
                    record.value = value;
                    record.recordState = 0;
                    // 入库记录
                    _putAuctionRecord(record.tokenId.AsByteArray(), record);
                    //保存主键
                    Storage.Put(Storage.CurrentContext, "AuctionRecord", record.tokenId.AsByteArray());
                    SaleBPECed(1, owner, value, record.tokenId, nowtime);
                    bol = true;
                }
            }
            // notify
            if (bol==false)
            {
                SaleBPECed(0, owner, value, recordKey, 0);
            }

            return bol;
        }

        /**
         * 取消购买BPEC
         */
        public static bool cancelBPEC(byte[] owner, BigInteger tokenId)
        {
            bool bol = false;
            if (Runtime.CheckWitness(owner) && owner.Length == 20)
            {
                object[] objInfo = getAuctionRecord(tokenId);
                if (objInfo.Length>0) {
                    AuctionRecord record = (AuctionRecord)(object)objInfo;
                    if (record.recordState==0 && owner== record.seller) {
                        object[] args = new object[3] { AuctionOwner, owner, record.value };
                        bool res = (bool)bpecCall("transfer", args);
                        if (res)
                        {
                            // 删除拍卖
                            Storage.Delete(Storage.CurrentContext, "buy".AsByteArray().Concat(tokenId.AsByteArray()));
                            //
                            CancelBPECed(1, owner, record.tokenId);
                            bol = true;
                        }
                    }
                }
            }
            // notify
            if (bol == false)
            {
                CancelBPECed(0, owner, tokenId);
            }

            return bol;
        }
        

        /**
         * 购买BPEC
         */
        public static bool buyBPEC(byte[] owner, BigInteger tokenId)
        {
            bool bol = false;
            if (Runtime.CheckWitness(owner) && owner.Length == 20)
            {
                object[] objInfo = getAuctionRecord(tokenId);
                if (objInfo.Length > 0)
                {
                    AuctionRecord record = (AuctionRecord)(object)objInfo;
                    if (record.recordState == 0)
                    {
                        UserInfo buyUser = getUserInfo(owner);
                        if (buyUser.balance>= record.sellPrice) {
                            object[] args = new object[3] { AuctionOwner, owner, record.value };
                            bool res = (bool)bpecCall("transfer", args);
                            if (res)
                            {
                                record.recordState = 1;
                                // 修改记录
                                _putAuctionRecord(record.tokenId.AsByteArray(), record);
                                //手续费
                                BigInteger fee = record.sellPrice * 3 / 100;
                                var money = record.sellPrice - fee;
                                _subTotal(fee);
                                //修改买家
                                buyUser.balance -= record.sellPrice;
                                var keyWho = new byte[] { 0x11 }.Concat(owner);
                                Storage.Put(Storage.CurrentContext, keyWho, Helper.Serialize(buyUser));
                                //修改卖家
                                UserInfo sellUser = getUserInfo(record.seller);
                                sellUser.balance += money;
                                keyWho = new byte[] { 0x11 }.Concat(record.seller);
                                Storage.Put(Storage.CurrentContext, keyWho, Helper.Serialize(sellUser));
                                BuyBPECed(1, owner, record.tokenId);
                                bol = true;
                            }
                        }
                    }
                }
            }
            // notify
            if (bol == false)
            {
                BuyBPECed(0, owner, tokenId);
            }

            return bol;
        }

        /**
         * 用户领取分红
         */
        public static bool recClaim(byte[] owner, BigInteger value)
        {
            bool bol = false;
            if (Runtime.CheckWitness(ContractOwner) && owner.Length > 0 && value > 0) { 
                UserInfo userInfo = getUserInfo(owner);
                userInfo.balance += value;
                var keyWho = new byte[] { 0x11 }.Concat(owner);
                Storage.Put(Storage.CurrentContext, keyWho, Helper.Serialize(userInfo));
                bol = true;
            }
            if (bol==true)
            {
                RecClaimed(1, owner, value);
            }
            else
            {
                RecClaimed(0, owner, value);
            }
            return bol;
        }

        /**
         * 获取拍卖成交记录
         */
        public static object[] getAuctionRecord(BigInteger tokenId)
        {
            var key = "buy".AsByteArray().Concat(tokenId.AsByteArray());
            byte[] v = Storage.Get(Storage.CurrentContext, key);
            if (v.Length == 0)
            {
                return new object[0];
            }
            return (object[])Helper.Deserialize(v);
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
         * 存储拍卖成交记录
         */
        private static void _putAuctionRecord(byte[] tokenId, AuctionRecord info)
        {
            // 新式实现方法只要一行
            byte[] txInfo = Helper.Serialize(info);

            var key = "buy".AsByteArray().Concat(tokenId);
            Storage.Put(Storage.CurrentContext, key, txInfo);
        }

        /**
         * 合约入口
         */
        public static Object Main(string method, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification) //取钱才会涉及这里
            {
                if (ContractOwner.Length == 20)
                {
                    // if param ContractOwner is script hash
                    //return Runtime.CheckWitness(ContractOwner);
                    return false;
                }
                else if (ContractOwner.Length == 33)
                {
                    // if param ContractOwner is public key
                    byte[] signature = method.AsByteArray();
                    return VerifySignature(signature, ContractOwner);
                }
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return true;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //必须在入口函数取得callscript，调用脚本的函数，也会导致执行栈变化，再取callscript就晚了
                var callscript = ExecutionEngine.CallingScriptHash;

                

                if (method == "_setSgas")
                {
                    if (Runtime.CheckWitness(ContractOwner))
                    {
                        Storage.Put(Storage.CurrentContext, "sgas", (byte[])args[0]);
                        return new byte[] { 0x01 };
                    }
                    return new byte[] { 0x00 };
                }
                if (method == "getSgas")
                {
                    return Storage.Get(Storage.CurrentContext, "sgas");
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
                //this is in nep5
                if (method == "totalExchargeSgas") return totalExchargeSgas();
                if (method == "version") return Version();
                if (method == "name") return name();
                if (method == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return balanceOf(account);
                }
                if (method == "getCoinPoolInfo")
                {
                    if (args.Length != 1) return 0;
                    BigInteger coinId = (BigInteger)args[0];
                    return getCoinPoolInfo(coinId);
                }
                if (method == "saleBPEC")
                {
                    if (args.Length != 3) return 0;
                    byte[] from = (byte[])args[0];
                    BigInteger price = (BigInteger)args[1];
                    BigInteger value = (BigInteger)args[2];
                    return saleBPEC(from, price, value);
                }
                if (method == "cancelBPEC")
                {
                    if (args.Length != 2) return 0;
                    byte[] from = (byte[])args[0];
                    BigInteger tokenId = (BigInteger)args[1];

                    return cancelBPEC(from, tokenId);
                }
                if (method == "buyBPEC")
                {
                    if (args.Length != 2) return 0;
                    byte[] owner = (byte[])args[0];
                    BigInteger tokenId = (BigInteger)args[1];
                    return buyBPEC(owner, tokenId);
                }
                if (method == "transferBPEC")
                {
                    if (args.Length != 2) return 0;
                    byte[] from = (byte[])args[0];
                    BigInteger value = (BigInteger)args[1];
                    return transferBPEC(from, value);
                }
                if (method == "recClaim")
                {
                    if (args.Length != 3) return 0;
                    byte[] owner = (byte[])args[0];
                    BigInteger value = (BigInteger)args[1];
                    return recClaim(owner, value);
                }
                if (method == "transferABC")
                {
                    if (args.Length != 5) return 0;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger tokenId = (BigInteger)args[2];
                    BigInteger pabc = (BigInteger)args[3];
                    BigInteger value = (BigInteger)args[4];
                    //没有from签名，不让转
                    if (!Runtime.CheckWitness(from))
                        return false;
                    //
                    byte[] callSave = Storage.Get(Storage.CurrentContext, callscript);
                    if (callSave.AsBigInteger() != callscript.AsBigInteger())
                        return false;

                    return transferABC(from, to, tokenId,pabc,value);
                }
                if (method == "drawToken")
                {
                    if (args.Length != 2) return 0;
                    byte[] owner = (byte[])args[0];
                    BigInteger count = (BigInteger)args[1];

                    return drawToken(owner, count);
                }

                if (method == "drawToContractOwner")
                {
                    if (args.Length != 2) return 0;
                    BigInteger flag = (BigInteger)args[0];
                    BigInteger count = (BigInteger)args[1];

                    return drawToContractOwner(flag, count);
                }
                if (method == "getAuctionAllFee")
                {
                    return getAuctionAllFee();
                }
                if (method == "rechargeToken")
                {
                    if (args.Length != 2) return 0;
                    byte[] owner = (byte[])args[0];
                    byte[] txid = (byte[])args[1];

                    return rechargeToken(owner, txid);
                }

                if (method == "hasAlreadyCharged")
                {
                    if (args.Length != 1) return 0;
                    byte[] txid = (byte[])args[0];

                    return hasAlreadyCharged(txid);
                }

                if (method == "getAuctionRecord")
                {
                    if (args.Length != 1)
                        return 0;
                    BigInteger txid = (BigInteger)args[0];
                    return getAuctionRecord(txid);
                }
                if (method == "balanceOfGold")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return balanceOfGold(account);
                }
                if (method == "upgrade")//合约的升级就是在合约中要添加这段代码来实现
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
                    string name = "DDZAuction";
                    string version = "1.1";
                    string author = "DDZ";
                    string email = "0";
                    string description = "DDZAuction";

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
            }
            return false;
        }

    }
}
