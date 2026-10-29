using System;

namespace PersonalInfoManager.Models
{




    public class SavedAccount
    {
        public Guid Id { get; set; } = Guid.NewGuid();


        public string ServiceName { get; set; } = "";


        public string Username { get; set; } = "";


        public string Password { get; set; } = "";


        public string Note { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public SavedAccount Clone() => (SavedAccount)MemberwiseClone();
    }
}