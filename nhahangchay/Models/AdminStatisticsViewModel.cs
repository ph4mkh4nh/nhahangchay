using System;
using System.Collections.Generic;

namespace nhahangchay.Models
{
    public class AdminStatisticsViewModel
    {
        public string Range { get; set; } = "month";
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public int TotalBookings { get; set; }
        public int PendingBookings { get; set; }
        public int CheckedInBookings { get; set; }
        public int CancelledBookings { get; set; }

        public decimal DepositCollected { get; set; }
        public decimal PendingDeposit { get; set; }
        public decimal EstimatedPreOrderTotal { get; set; }

        public int StandardBookings { get; set; }
        public int VipBookings { get; set; }
        public int OmakaseBookings { get; set; }

        public List<AdminDailyStatisticItem> DailyStats { get; set; } = new();
        public List<AdminMonthlyStatisticItem> MonthlyStats { get; set; } = new();
        public List<AdminBookingTypeStatisticItem> BookingTypeStats { get; set; } = new();
    }

    public class AdminDailyStatisticItem
    {
        public DateTime Day { get; set; }
        public int Total { get; set; }
        public int Pending { get; set; }
        public int CheckedIn { get; set; }
        public int Cancelled { get; set; }
        public decimal DepositCollected { get; set; }
    }

    public class AdminMonthlyStatisticItem
    {
        public DateTime Month { get; set; }
        public int Total { get; set; }
        public int Pending { get; set; }
        public int CheckedIn { get; set; }
        public int Cancelled { get; set; }
        public decimal DepositCollected { get; set; }
    }

    public class AdminBookingTypeStatisticItem
    {
        public string BookingType { get; set; } = "";
        public int Total { get; set; }
        public int CheckedIn { get; set; }
        public int Cancelled { get; set; }
        public decimal DepositCollected { get; set; }
    }
}