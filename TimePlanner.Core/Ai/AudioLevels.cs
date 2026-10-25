namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //pure utility for audio volume and speech detection calculations
    public static class AudioLevels
    {
        private const float SpeechRmsThreshold = 0.01f;
        private const double BlockDurationSeconds = 0.05;
        private const double RequiredSpeechDurationSeconds = 1.0;

        //-----------------------------
        //calculates root mean square power of float audio samples
        public static float Rms(ReadOnlySpan<float> samples)
        {
            //empty span has zero energy
            if (samples.IsEmpty)
            {
                return 0f;
            }

            var sum = 0.0;
            //accumulate squared sample magnitudes
            for (var i = 0; i < samples.Length; i++)
            {
                var s = samples[i];
                sum += s * s;
            }

            return (float)Math.Sqrt(sum / samples.Length);
        }

        //-----------------------------
        //determines whether audio is purely zeroes
        public static bool IsDigitalSilence(ReadOnlySpan<float> samples)
        {
            //empty audio is not considered digital silence
            if (samples.IsEmpty)
            {
                return false;
            }

            //check if all samples are exactly zero
            for (var i = 0; i < samples.Length; i++)
            {
                //any non zero value is not digital silence
                if (samples[i] != 0f)
                {
                    return false;
                }
            }

            return true;
        }

        //-----------------------------
        //detects whether speech is present based on accumulated blocks
        public static bool HasSpeech(ReadOnlySpan<float> samples, int sampleRate = 16000)
        {
            //empty or invalid sample rate cannot contain speech
            if (samples.IsEmpty || sampleRate <= 0)
            {
                return false;
            }

            var blockSize = (int)(sampleRate * BlockDurationSeconds);
            //block size must be positive
            if (blockSize <= 0)
            {
                return false;
            }

            var blocksRequired = (int)Math.Ceiling(RequiredSpeechDurationSeconds / BlockDurationSeconds);
            var loudBlocks = 0;

            //evaluate energy in each fifty millisecond block
            for (var offset = 0; offset + blockSize <= samples.Length; offset += blockSize)
            {
                var block = samples.Slice(offset, blockSize);
                var level = Rms(block);

                //increment count when block meets threshold
                if (level >= SpeechRmsThreshold)
                {
                    loudBlocks++;
                    //return true once required duration is reached
                    if (loudBlocks >= blocksRequired)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
//------------------------------EOF-----------------------------\\
