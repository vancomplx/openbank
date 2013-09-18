﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenBank
{
    /// <summary>
    /// STMTRS
    /// </summary>
    public class StatementResponse
    {
        /// <summary>
        /// LEDGERBAL
        /// </summary>
        public StatementBalance ledger_balance { get; set; }

        /// <summary>
        /// AVAILBAL
        /// </summary>
        public StatementBalance available_balance { get; set; }

        /// <summary>
        /// BANKTRANLIST
        /// </summary>
        public List<StatementTransaction> transactions { get; set; }
    }
}
