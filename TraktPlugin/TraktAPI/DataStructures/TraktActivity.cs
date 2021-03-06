﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace TraktPlugin.TraktAPI.DataStructures
{
    [DataContract]
    public class TraktActivity
    {
        [DataMember(Name = "timestamps")]
        public TraktTimestamps Timestamps { get; set; }

        [DataContract]
        public class TraktTimestamps
        {
            [DataMember(Name = "start")]
            public long Start { get; set; }

            [DataMember(Name = "current")]
            public long Current { get; set; }
        }

        [DataMember(Name = "activity")]
        public List<Activity> Activities { get; set; }

        [DataContract]
        public class Activity
        {
            [DataMember(Name = "timestamp")]
            public long Timestamp { get; set; }

            [DataMember(Name = "when")]
            public TraktWhen When { get; set; }

            [DataContract]
            public class TraktWhen
            {
                [DataMember(Name = "day")]
                public string Day { get; set; }

                [DataMember(Name = "time")]
                public string Time { get; set; }
            }

            [DataMember(Name = "elapsed")]
            public TraktElapsed Elapsed { get; set; }

            [DataContract]
            public class TraktElapsed
            {
                [DataMember(Name = "full")]
                public string Full { get; set; }

                [DataMember(Name = "short")]
                public string Short { get; set; }
            }

            [DataMember(Name = "type")]
            public string Type { get; set; }

            [DataMember(Name = "action")]
            public string Action { get; set; }

            [DataMember(Name = "user")]
            public TraktUserProfile User { get; set; }

            [DataMember(Name = "rating")]
            public string Rating { get; set; }

            [DataMember(Name = "episode")]
            public TraktEpisode Episode { get; set; }

            [DataMember(Name = "show")]
            public TraktShow Show { get; set; }

            [DataMember(Name = "movie")]
            public TraktMovie Movie { get; set; }

            [DataMember(Name = "shout")]
            public Activity.TraktShout Shout { get; set; }

            [DataContract]
            public class TraktShout
            {
                [DataMember(Name = "text")]
                public string Text { get; set; }

                [DataMember(Name = "spoiler")]
                public bool Spoiler { get; set; }
            }

            [DataMember(Name = "list")]
            public TraktList List { get; set; }

            [DataMember(Name = "list_item")]
            public Activity.TraktListItem ListItem { get; set; }

            [DataContract]
            public class TraktListItem
            {
                [DataMember(Name = "type")]
                public string Type { get; set; }

                [DataMember(Name = "show")]
                public TraktShow Show { get; set; }

                [DataMember(Name = "movie")]
                public TraktMovie Movie { get; set; }

                [DataMember(Name = "episode")]
                public TraktEpisode Episode { get; set; }
            }
        }

    }
}
