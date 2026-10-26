using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for audio volume calculations and speech presence
    public class AudioLevelsTests
    {
        //-----------------------------
        //digital silence detects all zeros
        [Fact]
        public void IsDigitalSilence_DetectsAllZeros()
        {
            var zeros = new float[16000];
            Assert.True(AudioLevels.IsDigitalSilence(zeros));
            Assert.False(AudioLevels.HasSpeech(zeros));
            Assert.Equal(0f, AudioLevels.Rms(zeros));
        }

        //-----------------------------
        //quiet room noise is neither digital silence nor speech
        [Fact]
        public void QuietRoomNoise_IsNotDigitalSilenceAndNotSpeech()
        {
            var noise = new float[16000 * 2];
            //fill with low amplitude white noise
            for (var i = 0; i < noise.Length; i++)
            {
                noise[i] = (i % 2 == 0) ? 0.002f : -0.002f;
            }

            Assert.False(AudioLevels.IsDigitalSilence(noise));
            Assert.False(AudioLevels.HasSpeech(noise));
            Assert.InRange(AudioLevels.Rms(noise), 0.001f, 0.003f);
        }

        //-----------------------------
        //quiet speech above threshold for at least one second is detected
        [Fact]
        public void QuietSpeechAboveThreshold_IsDetected()
        {
            var sampleRate = 16000;
            //duration of one point two seconds
            var samples = new float[(int)(sampleRate * 1.2)];
            //fill with amplitude exceeding speech threshold
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (i % 2 == 0) ? 0.015f : -0.015f;
            }

            Assert.False(AudioLevels.IsDigitalSilence(samples));
            Assert.True(AudioLevels.HasSpeech(samples, sampleRate));
        }

        //-----------------------------
        //short loud click does not count as speech
        [Fact]
        public void ShortClick_DoesNotCountAsSpeech()
        {
            var sampleRate = 16000;
            var samples = new float[sampleRate * 2];
            var clickLength = (int)(sampleRate * 0.1);

            //insert loud click for one hundred milliseconds
            for (var i = 0; i < clickLength; i++)
            {
                samples[i] = 0.5f;
            }

            Assert.False(AudioLevels.IsDigitalSilence(samples));
            Assert.False(AudioLevels.HasSpeech(samples, sampleRate));
        }
    }
}
//------------------------------EOF-----------------------------\\
